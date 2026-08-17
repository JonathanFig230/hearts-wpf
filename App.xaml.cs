using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace HeartsWpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (e.Args.Contains("--selftest"))
        {
            _ = RunSelfTestAsync();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
            "Hearts - Unexpected Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true; // don't let the whole app crash
    }

    /// <summary>
    /// Headless diagnostic run: play out full hands with all four seats
    /// auto-played (player 0 just takes its first valid option) and log any
    /// exception with a full stack trace to selftest.log. Launch with
    /// `HeartsWpf.exe --selftest`.
    /// </summary>
    private async Task RunSelfTestAsync()
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
        var log = new System.Text.StringBuilder();
        void Log(string s)
        {
            log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}");
            File.WriteAllText(logPath, log.ToString());
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        DispatcherUnhandledException += (s, e) =>
        {
            Log("DISPATCHER UNHANDLED EXCEPTION:\n" + e.Exception);
            e.Handled = true;
        };

        var engine = new GameEngine();
        int handsCompleted = 0;
        const int handsToRun = 2;
        var allDone = new TaskCompletionSource();
        bool busy = false;

        // Passing is fully synchronous (Toggle/Confirm never await before
        // Confirm), so it's handled procedurally right after the calls that
        // can start a hand, rather than reactively off Changed.
        void HandlePassingIfNeeded()
        {
            if (engine.Phase == GamePhase.Passing && !engine.HumanPassConfirmed)
            {
                foreach (var c in engine.Players[0].Hand.Take(3).ToList())
                    engine.ToggleHumanPassSelection(c);
                engine.ConfirmHumanPass();
            }
        }

        int lastLoggedTricks = -1;
        engine.Changed += () =>
        {
            if (engine.TricksPlayed != lastLoggedTricks)
            {
                lastLoggedTricks = engine.TricksPlayed;
                Log($"Tricks played: {engine.TricksPlayed}, phase={engine.Phase}, turn={engine.Turn}");
            }

            // PlayHumanCard's synchronous prefix (before its first await) fires
            // Changed again reentrantly; guard so we don't double-play into the
            // same trick. Forward progress for the *next* real state resumes
            // naturally once the pending Task.Delay elsewhere completes.
            if (busy) return;
            busy = true;
            try
            {
                if (engine.Phase == GamePhase.Playing && engine.Turn == 0 && engine.CurrentTrick.Count < 4)
                {
                    var valid = engine.ValidPlays(0);
                    if (valid.Count > 0) engine.PlayHumanCard(valid[0]);
                }
            }
            catch (Exception ex)
            {
                Log("EXCEPTION in Changed handler:\n" + ex);
                allDone.TrySetResult();
            }
            finally
            {
                busy = false;
            }
        };
        engine.HandCompleted += shooter =>
        {
            handsCompleted++;
            Log($"Hand {handsCompleted} complete. Shooter={shooter}. Scores: " +
                string.Join(", ", engine.Players.Select(p => $"{p.Name}={p.TotalScore}")));
            if (handsCompleted >= handsToRun)
            {
                allDone.TrySetResult();
                return;
            }
            try
            {
                engine.ContinueAfterRound();
                HandlePassingIfNeeded();
            }
            catch (Exception ex)
            {
                Log("EXCEPTION after ContinueAfterRound:\n" + ex);
                allDone.TrySetResult();
            }
        };
        engine.GameCompleted += () =>
        {
            Log("Game completed.");
            allDone.TrySetResult();
        };

        Log("Self-test starting...");
        try
        {
            engine.NewGame();
            HandlePassingIfNeeded();
        }
        catch (Exception ex)
        {
            Log("EXCEPTION during startup:\n" + ex);
            allDone.TrySetResult();
        }

        var completed = await Task.WhenAny(allDone.Task, Task.Delay(TimeSpan.FromSeconds(90)));
        Log(completed == allDone.Task ? "Self-test finished." : "Self-test TIMED OUT.");

        Shutdown();
    }
}
