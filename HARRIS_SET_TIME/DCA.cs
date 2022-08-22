using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HARRIS_SET_TIME
{
    public class Dca {
        public Dca(dynamic config)
        {
            user = config.user;
            password = config.password;
            number = config.number;
        }
        public string user { get; set; }
        public string password { get; set; }
        public string number { get; set; }
    }
}
