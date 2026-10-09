using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public enum ConsoleBotStage { Disabled, Stopped, Starting, Running, Stopping, Faulted }

    // Owns one console loop at a time. Discord has a separate lifetime so a failed
    // console connection can be restarted from the control panel.
    public sealed class ConsoleBotController
    {
        private readonly object _sync = new();
        private readonly Func<CancellationToken, Task> _run;
        private readonly Action _disconnect;
        private readonly Action _pause;
        private readonly Func<bool> _activeOrder;
        private readonly Action<Exception> _log;
        private readonly bool _enabled;
        private bool _wanted;
        private bool _stop;
        private bool _restart;
        private ConsoleBotStage _stage;

        public ConsoleBotController(bool enabled, Func<CancellationToken, Task> run,
            Action disconnect, Action pause, Func<bool> activeOrder, Action<Exception> log)
        {
            _enabled = enabled;
            _wanted = enabled;
            _stage = enabled ? ConsoleBotStage.Starting : ConsoleBotStage.Disabled;
            _run = run; _disconnect = disconnect; _pause = pause; _activeOrder = activeOrder; _log = log;
        }

        public ConsoleBotStage Stage { get { lock (_sync) return _stage; } }
        public bool CanAcceptOrders { get { lock (_sync) return _wanted && !_stop && _stage is ConsoleBotStage.Starting or ConsoleBotStage.Running; } }

        public string Start()
        {
            lock (_sync)
            {
                if (!_enabled) return "Console control is disabled by SkipConsoleBotCreation. Change the configuration and restart the application.";
                if (_stop) return "The bot is stopping. Wait for it to finish before starting it again.";
                _wanted = true;
                if (_stage is ConsoleBotStage.Stopped or ConsoleBotStage.Faulted) _stage = ConsoleBotStage.Starting;
                return "The bot is starting or already running.";
            }
        }

        public string Stop(bool restart = false)
        {
            lock (_sync)
            {
                if (!_enabled) return "Console control is disabled by SkipConsoleBotCreation.";
                _pause();
                _restart = restart;
                _wanted = restart;
                _stop = _stage is ConsoleBotStage.Starting or ConsoleBotStage.Running or ConsoleBotStage.Stopping;
                _stage = _stop ? ConsoleBotStage.Stopping : restart ? ConsoleBotStage.Starting : ConsoleBotStage.Stopped;
                return restart
                    ? "The console connection will restart after the current order finishes. Requests are paused, use Resume requests when ready."
                    : "The bot will stop after the current order finishes. Discord and the waiting queue stay available.";
            }
        }

        public void MarkRunning()
        {
            lock (_sync) if (!_stop) _stage = ConsoleBotStage.Running;
        }

        public bool TryStartOrder(Func<bool> dequeue)
        {
            lock (_sync) return !_stop && _wanted && dequeue();
        }

        public async Task RunAsync(CancellationToken token)
        {
            Task? session = null;
            CancellationTokenSource? sessionCancellation = null;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    lock (_sync)
                    {
                        if (_stop && session == null)
                        {
                            _stop = false;
                            _wanted = _restart;
                            _restart = false;
                            _stage = _wanted ? ConsoleBotStage.Starting : ConsoleBotStage.Stopped;
                        }
                        if (session == null && _wanted && !_stop)
                        {
                            _stage = ConsoleBotStage.Starting;
                            sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
                            var sessionToken = sessionCancellation.Token;
                            session = Task.Run(() => _run(sessionToken), CancellationToken.None);
                        }
                        if (_stop && session != null && !_activeOrder()) sessionCancellation!.Cancel();
                    }

                    if (session?.IsCompleted == true)
                    {
                        Exception? failure = null;
                        try { await session.ConfigureAwait(false); }
                        catch (OperationCanceledException) when (sessionCancellation!.IsCancellationRequested) { }
                        catch (Exception ex) { failure = ex; _log(ex); }
                        try { _disconnect(); }
                        catch (Exception ex) { failure = ex; _log(ex); }
                        lock (_sync)
                        {
                            bool requested = _stop;
                            _wanted = requested && _restart;
                            _stop = false; _restart = false;
                            _stage = _wanted ? ConsoleBotStage.Starting : requested && failure == null ? ConsoleBotStage.Stopped : ConsoleBotStage.Faulted;
                            if (_stage == ConsoleBotStage.Faulted) _pause();
                        }
                        sessionCancellation!.Dispose();
                        sessionCancellation = null;
                        session = null;
                    }
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            finally
            {
                if (sessionCancellation != null)
                {
                    sessionCancellation.Cancel();
                    try { if (session != null) await session.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { _log(ex); }
                    sessionCancellation.Dispose();
                }
                try { _disconnect(); }
                catch (Exception ex) { _log(ex); }
                lock (_sync) { _wanted = false; _stop = false; _stage = _enabled ? ConsoleBotStage.Stopped : ConsoleBotStage.Disabled; }
            }
        }
    }
}
