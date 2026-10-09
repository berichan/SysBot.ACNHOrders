using System;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    internal static class OrderInteractionGate
    {
        internal static async Task<bool> EnterAsync(SemaphoreSlim gate, bool opensModal, Func<Task> acknowledge, Func<Task> busy)
        {
            // Opening a modal must be the first response. Do not wait on a slow action.
            if (opensModal)
            {
                if (gate.Wait(0)) return true;
                await busy().ConfigureAwait(false);
                return false;
            }
            await acknowledge().ConfigureAwait(false);
            await gate.WaitAsync().ConfigureAwait(false);
            return true;
        }
    }
}
