using System.Collections.Generic;

namespace ClashChecker;

public partial class ClashChecker
{
    public record ZoneCollision { 
        public int InitialZone { get; set; }
        public List<int> CollidedZones { get; set; }
    
    }

}