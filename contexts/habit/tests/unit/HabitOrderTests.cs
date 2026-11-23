#pragma warning disable xUnit1051 // tests run to completion; no cancellation token needed
using Ctx.Habit.Domain;
using LazyTax.Platform.Problems;

namespace Ctx.Habit.Tests.Unit;

public sealed class HabitOrderTests
{
  private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid(), C = Guid.NewGuid();

  [Fact]
  public void Requested_first_then_the_rest_in_current_order() =>
    HabitOrder.Merge([A, B, C], [C]).Should().Equal(C, A, B);

  [Fact]
  public void Empty_request_keeps_the_order() => HabitOrder.Merge([A, B], []).Should().Equal(A, B);

  [Fact]
  public void Duplicate_is_refused() =>
    ((Action)(() => HabitOrder.Merge([A, B], [A, A]))).Should().Throw<ValidationFailed>();

  [Fact]
  public void Unknown_or_foreign_id_is_refused() =>
    ((Action)(() => HabitOrder.Merge([A, B], [C]))).Should().Throw<ValidationFailed>();
}
