using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Domain
{
    // Shared user status enum used by UserService and OrderService integration events.
    public enum UserStatus
    {
        Active = 0,
        Inactive = 1
    }
}
