using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public interface IDodoRestoreNotifier
    {
        void NotifyServerOfState(GameState gs);
        void NotifyServerOfDodoCode(string dodo);
    }
}
