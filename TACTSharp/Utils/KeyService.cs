﻿namespace TACTSharp
{
    public static class KeyService
    {
        static KeyService()
        {
            if (keys.Count == 0)
                LoadKeys();
        }

        private static readonly Dictionary<ulong, byte[]> keys = [];
        private static readonly Dictionary<string, byte[]> ArmadilloKeys = new();

        public static Salsa20 SalsaInstance { get; } = new Salsa20();

        // TACT keys
        public static bool TryGetKey(ulong keyName, out byte[] key)
        {
#pragma warning disable CS8601 // Possible null reference assignment.
            return keys.TryGetValue(keyName, out key);
#pragma warning restore CS8601
        }

        public static void SetKey(ulong keyName, byte[] key)
        {
            if (key == null || key.Length == 0)
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            keys[keyName] = key;
        }

        // Armadillo keys
        public static bool TryGetArmadilloKey(string name, out byte[] key)
        {
#pragma warning disable CS8601 // Possible null reference assignment.
            return ArmadilloKeys.TryGetValue(name, out key);
#pragma warning restore CS8601
        }

        public static void SetArmadilloKey(string name, byte[] key)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Name cannot be null or empty", nameof(name));
            if (key == null || key.Length == 0)
                throw new ArgumentException("Key cannot be null or empty", nameof(key));

            ArmadilloKeys[name] = key;
        }

        public static void LoadKeys()
        {
            if (!File.Exists("WoW.txt")) return;

            foreach (var line in File.ReadAllLines("WoW.txt"))
            {
                var splitLine = line.Split(' ');
                var lookup = ulong.Parse(splitLine[0], System.Globalization.NumberStyles.HexNumber);
                byte[] key = Convert.FromHexString(splitLine[1].Trim());
                keys.TryAdd(lookup, key);
            }
        }
    }
}