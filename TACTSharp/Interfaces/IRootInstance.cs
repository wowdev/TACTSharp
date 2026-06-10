using static TACTSharp.RootInstance;

namespace TACTSharp.Interfaces
{
    public interface IRootInstance
    {
        public List<RootEntry> GetEntriesByFDID(uint fileDataID);
        public List<RootEntry> GetEntriesByLookup(ulong lookup);
        public uint[] GetAvailableFDIDs();
        public ulong[] GetAvailableLookups();
        public bool FileExists(ulong lookup);
        public bool FileExists(uint fileDataID);
    }
}
