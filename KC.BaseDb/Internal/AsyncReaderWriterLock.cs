using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KC.BaseDb.Internal {
    /// <summary>
    /// The simplest KISS version of this I could come up with.
    /// I'd rather have it be stupid simple with rare poor performance than complex
    /// with a chance of locking up or not working.
    /// </summary>
    public class AsyncReaderWriterLock : IDisposable {
        private object lockEverything = new object();
        private SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private int reads;

        public void Dispose() {
            writeLock.Dispose();
        }

        public async Task<Action> EnterReadLock() {
            await writeLock.WaitAsync();
            try {
                lock (lockEverything) {
                    reads++;
                }
            }
            finally {
                writeLock.Release();
            }
            return this.ReleaseReadLock;
        }

        public void ReleaseReadLock() {
            lock (lockEverything) {
                reads = Math.Max(0, reads - 1);
            }
        }

        public async Task<Action> EnterWriteLock() {
            await writeLock.WaitAsync();
            while (true) {
                lock (lockEverything) {
                    if (reads == 0) {
                        break;
                    }
                }
                await Task.Delay(1);
            }
            return this.ReleaseWriteLock;
        }

        public void ReleaseWriteLock() {
            writeLock.Release();
        }
    }
}
