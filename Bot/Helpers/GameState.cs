using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public enum GameState
    {
        Idle,
        Fetching,
        Active,
        Faulted,
        TimedOut
    }
}
