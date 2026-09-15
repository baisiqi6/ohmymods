using System;

// Il2Cpp 容器替身：签名与 interop 的 Il2CppSystem.Collections.Generic.List<T> 一致
// （Count 属性 + 索引器 + 结构修改方法）。额外带写计数，供测试断言“只读候选表”。

namespace Il2CppSystem.Collections.Generic
{
    public class List<T>
    {
        private readonly System.Collections.Generic.List<T> _items =
            new System.Collections.Generic.List<T>();

        /// <summary>任何元素/结构写入计数（生产代码必须保持 0）。</summary>
        public int Writes;

        /// <summary>读取计数（Count + 索引器），用于断言“没读那条表”。</summary>
        public int Reads;

        /// <summary>模拟原生容器读取失败：容器访问必须被上层捕获。</summary>
        public bool ThrowOnRead;

        public int Count
        {
            get
            {
                Reads++;
                if (ThrowOnRead) throw new InvalidOperationException("interop list read boom");
                return _items.Count;
            }
        }

        public T this[int index]
        {
            get
            {
                Reads++;
                if (ThrowOnRead) throw new InvalidOperationException("interop list read boom");
                return _items[index];
            }
            set
            {
                Writes++;
                _items[index] = value;
            }
        }

        public void Add(T item)
        {
            Writes++;
            _items.Add(item);
        }

        public void Insert(int index, T item)
        {
            Writes++;
            _items.Insert(index, item);
        }

        public bool Remove(T item)
        {
            Writes++;
            return _items.Remove(item);
        }

        public void RemoveAt(int index)
        {
            Writes++;
            _items.RemoveAt(index);
        }

        public void Clear()
        {
            Writes++;
            _items.Clear();
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }
    }
}
