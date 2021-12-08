using System.Collections.Generic;

namespace FilingHostService
{
    public class StatuteCode
    {
        public string BaseWord { get; set; }
        public List<string> Prefixes { get; set; }
        public string Name { get; set; }
        public decimal Code { get; set; }
        public string SequenceID { get; set; }
        public List<StatuteCode> AdditionalStatutes { get; set; }
        public string PrefixedWord
        {
            get
            {
                if (this.Prefixes.Count > 0)
                {
                    return string.Format("{0}-{1}", string.Join("-", this.Prefixes), this.BaseWord);
                }
                else
                {
                    return this.BaseWord;
                }
            }
        }
    }
}
