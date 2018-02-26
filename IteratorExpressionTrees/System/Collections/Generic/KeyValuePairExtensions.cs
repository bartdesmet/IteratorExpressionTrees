namespace System.Collections.Generic
{
    public static class KeyValuePairExtensions
    {
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> kv, out K key, out V value)
        {
            key = kv.Key;
            value = kv.Value;
        }
    }
}
