using System;
using System.Linq;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    internal static class QuickOrderSubmission
    {
        internal static void ApplyInput(OrderDraft draft, string input, CrossBotConfig config)
        {
            var parsed = OrderPreparation.Parse(input, config.DropConfig, config.Prefix);
            draft.Input = input;
            draft.Items = OrderPreparation.FullStacks(parsed.Items);
            draft.Errors = parsed.Errors;
            draft.Language = OrderPreparation.Languages.Contains(parsed.Language) ? parsed.Language : "en";
            draft.Villager = parsed.Villager;
            draft.Mode = parsed.Mode ?? OrderFillMode.Standard;
            draft.PendingItem = null;
            draft.Preview = null;
            draft.PreviewRevision = -1;
        }

        internal static async Task<QueueAttemptResult> SubmitAsync(OrderDraft draft, CrossBotConfig config,
            string username, Func<PreparedOrder, Task<QueueAttemptResult>> enqueue)
        {
            if (draft.Errors.Length > 0)
                return new(string.Join("\n", draft.Errors), false);
            if (!OrderPreparation.TryPrepare(draft.Items, draft.Mode, config, username, draft.Villager, out var prepared, out var error))
                return new(error, false);
            return await enqueue(prepared!).ConfigureAwait(false);
        }
    }
}
