using System;

namespace Polymetal.Core.Client.Messenger
{
    internal class TriggerState
    {
        internal enum SourceEnum
        {
            Pdms,
            Tekla,
            Revit, 
            MagiCAD
        }

        private SourceEnum _source { get; }
        internal SourceEnum Source => _source;

        private int _id { get; }
        internal int Id => _id;

        private byte _state;

        internal byte State
        {
            get => _state;
            set => _state = value;
        }

        internal TriggerState(int id, byte state, string source)
        {
            _id = id;
            _state = state;

            _source = (SourceEnum)Enum.Parse(typeof(SourceEnum), source);
        }
    }
}
