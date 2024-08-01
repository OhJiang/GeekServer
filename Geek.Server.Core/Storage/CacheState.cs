using Geek.Server.Core.Serialize;
using MessagePack;
using MongoDB.Bson.Serialization.Attributes;
using NLog;

namespace Geek.Server.Core.Storage
{
    /// <summary>
    /// 表示缓存状态的抽象类，包含对象的唯一标识符、哈希状态管理以及相关的序列化和数据库交互功能。
    /// </summary>
    [MessagePackObject(true)]
    [BsonIgnoreExtraElements(true, Inherited = true)]
    public abstract class CacheState
    {
        /// <summary>
        /// 表示唯一标识符的常量字段。
        /// </summary>
        public const string UniqueId = nameof(Id);

        /// <summary>
        /// 获取或设置对象的唯一标识符。
        /// </summary>
        public long Id { get; set; }

        /// <summary>
        /// 返回表示当前对象的字符串。
        /// </summary>
        /// <returns>表示当前对象的字符串。</returns>
        public override string ToString()
        {
            return $"{base.ToString()}[Id={Id}]";
        }

        #region hash 

        private StateHash stateHash;

        /// <summary>
        /// 从数据库加载数据后初始化哈希状态。
        /// </summary>
        public void AfterLoadFromDB()
        {
            stateHash ??= new StateHash(this, true);
        }

        /// <summary>
        /// 检查对象是否有变化。
        /// </summary>
        /// <returns>如果对象发生变化，返回 true；否则返回 false。</returns>
        public bool IsChanged()
        {
            stateHash ??= new StateHash(this, false);
            return stateHash.IsChanged();
        }

        /// <summary>
        /// 将当前哈希状态设置为数据库保存的状态。
        /// </summary>
        public void AfterSaveToDB()
        {
            stateHash.AfterSaveToDB();
        }

        #endregion
    }

    /// <summary>
    /// 用于计算和管理 CacheState 对象的哈希值，以便检测对象是否发生变化。
    /// </summary>
    public class StateHash
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private CacheState State { get; }
        private UInt128 CurrentHash { get; set; }
        private UInt128 DBHash { get; set; }

        /// <summary>
        /// 初始化 StateHash 对象，并根据参数决定是否从数据库加载哈希值。
        /// </summary>
        /// <param name="state">缓存状态对象。</param>
        /// <param name="loadFromDB">是否从数据库加载哈希值。</param>
        public StateHash(CacheState state, bool loadFromDB = false)
        {
            State = state;
            CurrentHash = GetHash();
            if (loadFromDB)
            {
                DBHash = CurrentHash;
            }
        }

        /// <summary>
        /// 检查当前哈希值是否与数据库中的哈希值不同，确定对象是否发生变化。
        /// </summary>
        /// <returns>如果对象发生变化，返回 true；否则返回 false。</returns>
        public bool IsChanged()
        {
            if (DBHash != CurrentHash)
                return true;
            CurrentHash = GetHash();
            return DBHash != CurrentHash || CurrentHash == 0;
        }

        /// <summary>
        /// 将当前哈希值设置为数据库保存的哈希值。
        /// </summary>
        public void AfterSaveToDB()
        {
            DBHash = CurrentHash;
        }

        /// <summary>
        /// 用于计算对象序列化后的哈希值的流类。
        /// </summary>
        public class HashStream : Stream
        {
            public ulong hash = 3074457345618258791ul;

            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => 0;
            public override long Position { get; set; }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => 0;
            public override void SetLength(long value) { }

            /// <summary>
            /// 重写 Write 方法，通过对每个字节进行哈希计算来更新哈希值。
            /// </summary>
            /// <param name="buffer">要写入的数据缓冲区。</param>
            /// <param name="offset">开始写入的字节偏移量。</param>
            /// <param name="count">要写入的字节数。</param>
            public override void Write(byte[] buffer, int offset, int count)
            {
                Position += count;
                for (int i = offset; i < count; i++)
                {
                    hash += buffer[i];
                    hash *= 3074457345618258799ul;
                }
            }
        }

        /// <summary>
        /// 计算 CacheState 对象的哈希值。
        /// </summary>
        /// <returns>返回计算的哈希值。</returns>
        unsafe private UInt128 GetHash()
        {
            if (State == null)
                return 0;
            try
            {
                var hashSteam = new HashStream();
                Serializer.Serialize(hashSteam, State);
                return new UInt128(hashSteam.hash, (ulong)hashSteam.Position);
            }
            catch (Exception e)
            {
                Log.Error($"GetHash异常, type: [{State.GetType().FullName}]: {e.Message}");
            }
            return 0;
        }
    }
}
