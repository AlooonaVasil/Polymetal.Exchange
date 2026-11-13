using System.Collections.Generic;

namespace Polymetal.Exchange.Revit.v2023.DTO
{
    internal class RevitRoom
    {
        internal string Guid;
        internal int Id;
        internal bool Activity;
        internal Dictionary<int, string> Properties;

        internal RevitRoom(string guid, int id, bool activity, Dictionary<int, string> properties)
        {
            Guid = guid;
            Id = id;
            Activity = activity;
            Properties = properties;
        }
    }
}
