using System;
using System.Collections.Generic;
using System.Text;

namespace KC.BaseDb {
    public struct Notification {
        public string Topic { get; set; }
        public string Payload { get; set; }
    }
}
