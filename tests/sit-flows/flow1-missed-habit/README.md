# Flow 1: a missed habit

This collection runs the black-box part only: sign up, configuration, a habit with a 5.00 stake, and a cron tick that answers 200. The app runs on the real clock, so the habit's first deadline is still ahead and A has no failed execution yet.

The missed part needs the clock past the deadline. It lives in `contexts/check/tests/integration/MissedHabitFlowTests.cs`: the whole app on real Postgres, the same routes and bodies, then `CheckService.Tick(tomorrow 01:00 UTC)` called directly. It asserts the failed execution, the pending penalty, and, after the penalty processor charges it, the inbox item. The app has no time override.
