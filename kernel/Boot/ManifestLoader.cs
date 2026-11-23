using System.Reflection;
using System.Text.RegularExpressions;
using LazyTax.Platform.Context;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LazyTax.Kernel.Boot;

/// <summary>Reads each context's embedded context.yaml and checks it (fields, names, needs, uses, cycles).</summary>
public static partial class ManifestLoader
{
  [GeneratedRegex("^[a-z][a-z0-9]*$")]
  private static partial Regex NameRx();

  [GeneratedRegex("^ctx_[a-z0-9]{6,}$")]
  private static partial Regex IdRx();

  [GeneratedRegex("^[a-z][a-z0-9]*\\.v[0-9]+$")]
  private static partial Regex ContractRx();

  [GeneratedRegex("^[a-z][a-z0-9]*\\.[a-z][a-z0-9_.]*$")]
  public static partial Regex EventRx();

  private sealed class Yaml
  {
    public string? Id { get; set; }
    public string? Name { get; set; }
    public List<string>? Needs { get; set; }
    public List<string>? Provides { get; set; }
    public List<string>? Uses { get; set; }
    public Prefix? Http { get; set; }
    public Prefix? Mcp { get; set; }
  }

  private sealed class Prefix
  {
    [YamlMember(Alias = "prefix")]
    public string? Value { get; set; }
  }

  public static (ContextManifest Manifest, IContext Context) Read(IContext ctx, List<ContextProblem> problems)
  {
    var asm = ContextAssembly(ctx);
    var folder = asm.GetCustomAttributes<AssemblyMetadataAttribute>()
      .FirstOrDefault(a => a.Key == "ContextFolder")?.Value ?? $"<{asm.GetName().Name}>";
    var folderName = folder.Split('/').Last();
    using var stream = asm.GetManifestResourceStream("context.yaml");
    if (stream is null)
    {
      problems.Add(new(folderName, folder, "context.yaml is missing", $"add {folder}/context.yaml (see docs/adding-a-context.md)"));
      return (new ContextManifest { Id = "", Name = folderName, Folder = folder }, ctx);
    }

    Yaml y;
    try
    {
      var text = new StreamReader(stream).ReadToEnd();
      y = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build().Deserialize<Yaml>(text) ?? new Yaml();
    }
    catch (Exception e)
    {
      problems.Add(new(folderName, folder, $"context.yaml does not parse: {e.Message}",
        "use only the fields id, name, needs, provides, uses, http.prefix, mcp.prefix"));
      return (new ContextManifest { Id = "", Name = folderName, Folder = folder }, ctx);
    }

    var m = new ContextManifest
    {
      Id = y.Id ?? "",
      Name = y.Name ?? "",
      Needs = y.Needs ?? [],
      Provides = y.Provides ?? [],
      Uses = y.Uses ?? [],
      HttpPrefix = y.Http?.Value,
      McpPrefix = y.Mcp?.Value,
      Folder = folder,
    };
    void P(string problem, string fix) => problems.Add(new(m.Name.Length > 0 ? m.Name : folderName, folder, problem, fix));

    if (!IdRx().IsMatch(m.Id)) P($"id '{m.Id}' is not like ctx_7f3k2q", "set id: ctx_ plus 6 random lowercase letters or digits, once, and never change it");
    if (!NameRx().IsMatch(m.Name)) P($"name '{m.Name}' is not lowercase letters and digits", "use the folder name");
    else if (m.Name != folderName) P($"name '{m.Name}' differs from the folder '{folderName}'", $"set name: {folderName}");
    var expectedType = $"Ctx.{Pascal(folderName)}.{Pascal(folderName)}Context";
    if (ctx is not PendingContext && ctx.GetType().FullName != expectedType) P($"the registration class is {ctx.GetType().FullName}", $"name it {expectedType} in src/Context.cs");
    foreach (var c in m.Needs.Concat(m.Provides))
      if (!ContractRx().IsMatch(c)) P($"contract '{c}' is not like plan.v1", "write <context>.v<number>");
    foreach (var c in m.Provides)
      if (!c.StartsWith(m.Name + ".", StringComparison.Ordinal)) P($"provides '{c}', which is another context's name", $"provide only {m.Name}.v<n>");
    if (m.HttpPrefix is { } hp && !hp.StartsWith("/api/v", StringComparison.Ordinal)) P($"http.prefix '{hp}' is not under /api/v<n>/", $"use /api/v1/{m.Name}");
    if (m.McpPrefix is { } mp && mp != m.Name) P($"mcp.prefix '{mp}' differs from the name", $"set mcp.prefix: {m.Name}");
    if (m.Uses.Count == 0) P("uses is empty", "list the infrastructure from dependency.yaml, e.g. uses: [postgres.main]");
    return (m, ctx);
  }

  /// <summary>The assembly that carries the context's context.yaml (its src project).</summary>
  public static Assembly ContextAssembly(IContext ctx) => ctx is PendingContext p ? p.Assembly : ctx.GetType().Assembly;

  public static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

  /// <summary>Checks needs against provides and dependency.yaml, then orders contexts so needs come first.</summary>
  public static List<(ContextManifest Manifest, IContext Context)> Order(
    List<(ContextManifest Manifest, IContext Context)> all, ISet<string> dependencies, List<ContextProblem> problems)
  {
    var providers = new Dictionary<string, ContextManifest>();
    foreach (var (m, _) in all)
      foreach (var c in m.Provides)
        if (!providers.TryAdd(c, m))
          problems.Add(new(m.Name, m.Folder, $"provides {c}, which {providers[c].Name} provides too", "a contract has one provider; rename or remove one"));
    var names = new HashSet<string>();
    foreach (var (m, _) in all)
    {
      if (!names.Add(m.Name)) problems.Add(new(m.Name, m.Folder, "two contexts share this name", "rename one folder"));
      foreach (var n in m.Needs)
        if (!providers.ContainsKey(n))
          problems.Add(new(m.Name, m.Folder, $"needs {n}, but no context provides it",
            $"add the context that provides {n}, or remove it from needs"));
      foreach (var u in m.Uses)
        if (!dependencies.Contains(u))
          problems.Add(new(m.Name, m.Folder, $"uses {u}, which dependency.yaml does not list", $"add {u} to dependency.yaml, or remove it from uses"));
    }

    var byName = all.GroupBy(x => x.Manifest.Name).ToDictionary(g => g.Key, g => g.First());
    var ordered = new List<(ContextManifest, IContext)>();
    var state = new Dictionary<string, int>(); // 1 visiting, 2 done
    void Visit(string name, Stack<string> path)
    {
      if (state.GetValueOrDefault(name) == 2) return;
      if (state.GetValueOrDefault(name) == 1)
      {
        var m = byName[name].Manifest;
        problems.Add(new(name, m.Folder, $"needs form a cycle: {string.Join(" -> ", path.Reverse().Append(name))}",
          "break it with an outbox event or a kernel hook (see docs/adding-a-context.md)"));
        return;
      }
      state[name] = 1;
      path.Push(name);
      foreach (var n in byName[name].Manifest.Needs)
        if (providers.TryGetValue(n, out var p) && p.Name != name) Visit(p.Name, path);
      path.Pop();
      state[name] = 2;
      ordered.Add(byName[name]);
    }
    foreach (var name in byName.Keys.Order(StringComparer.Ordinal)) Visit(name, new Stack<string>());
    return ordered;
  }
}
