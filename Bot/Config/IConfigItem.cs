using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public interface IConfigItem
    {
        bool WrapAllItems { get; }
        ItemWrappingPaper WrappingPaper { get; }
    }
}
