using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KC.BaseDb {
    public class BaseModelWithDates : BaseModel {
        public BaseModelWithDates() {
            this.CreatedUtc = DateTimeOffset.Now;
            this.ModifiedUtc = CreatedUtc;
        }

        public DateTimeOffset CreatedUtc { get; set; }
        public DateTimeOffset ModifiedUtc { get; set; }
    }
}
