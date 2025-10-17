using System;

namespace BudgetUpdater.DesktopApp
{
    public class BudgetInterop
    {
        private readonly Action<string> _log;
        private readonly Action<object?> _refresh;

        public BudgetInterop(Action<string> log, Action<object?> refresh)
        {
            _log = log;
            _refresh = refresh;
        }

        public void Log(string message) => _log?.Invoke(message);
        public void Refresh(object? data) => _refresh?.Invoke(data);
    }
}
