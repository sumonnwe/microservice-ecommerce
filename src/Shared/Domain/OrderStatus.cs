using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Domain
{
    public enum OrderStatus
    {
        Pending = 0,
        Completed = 1,
        Paid = 2,
        Cancelled = 3
    }
}

