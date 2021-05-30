using System.Collections;
using System.Collections.Generic;

namespace ReadmeSample.Entities
{
    public class User
    {
        public int Id { get; set; }

        public string UserName { get; set; }

        public string EmailAddress { get; set; }

        public ICollection<Order> Orders { get; set; }
    }
}
