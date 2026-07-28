using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VIewModel
{
    public class ClashStatistics
    {
        public int Total {  get; set; }
        public int New {  get; set; }
        public int Request { get; set; }
        public int RequestOut { get; set; }
        public int Approve { get; set; }
        public int InWork { get; set; }
        public int InWorkOut { get; set; }
    }
}
