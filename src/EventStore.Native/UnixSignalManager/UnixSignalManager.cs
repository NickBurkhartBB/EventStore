using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Mono.Unix;
using Mono.Unix.Native;

namespace EventStore.Native.UnixSignalManager {
	public class UnixSignalManager {
		private UnixSignal[] _handledSignals;
		private Dictionary<Signum, List<Action>> _actions;
		private volatile bool _stop;

		public UnixSignalManager(Signum[] handledSignums) {
			_handledSignals = handledSignums.Select(x => new UnixSignal(x)).ToArray();
			_actions = new Dictionary<Signum, List<Action>>();
			new Thread(HandleSignals).Start();
		}

		public void Subscribe(Signum signum, Action action) {
			if (_handledSignals.All(x => x.Signum != signum)) {
				throw new ArgumentException($"The signal: {signum} is not currently being handled.");
			}

			if (_actions.TryGetValue(signum, out List<Action> actions)) {
				actions.Add(action);
			} else {
				_actions[signum] = new List<Action> { action };
			}
		}
		public void Stop() {
			_stop = true;
		}

		private void HandleSignals() {
			const int timeoutMs = 1000;

			while (!_stop) {
				var index = UnixSignal.WaitAny(_handledSignals, TimeSpan.FromMilliseconds(timeoutMs));
				if (index == timeoutMs) continue;
				if (_actions.TryGetValue(_handledSignals[index].Signum, out List<Action> actions)) {
					foreach (var action in actions) {
						action.Invoke();
					}
				}
				_handledSignals[index].Reset();
			}
		}
	}
}
