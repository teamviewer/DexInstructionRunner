using System;

namespace DexInstructionRunner.Models
{
    public class OtherResponseItem
    {
        public string Device { get; set; } = "";
        public DateTime? ResponseTime { get; set; }
        public string Message { get; set; } = "";
        public int Status { get; set; }
        public string StatusText =>
    
            Status switch
                {
                    1 => "Success (No Content)",
                    2 => "Error",
                    3 => "Not Implemented",
                    4 => "Response Too Large",
                    _ => Status.ToString()
                };
    }


}

