using System.Collections.Generic;
using System.Linq;

namespace Fantasia.Combat
{
    // Speed sets initial turn order (GDD 6.1); ties keep the input order, so
    // callers should build the list via BattleFormation's left-to-right
    // sequence to echo For the King's formation tiebreak.
    public class TurnQueue
    {
        private readonly List<Combatant> _order;
        private int _index = -1;

        public TurnQueue(IEnumerable<Combatant> combatants)
        {
            _order = combatants.OrderByDescending(c => c.Speed).ToList();
        }

        public Combatant Current => _order[_index];

        public Combatant Advance()
        {
            do
            {
                _index = (_index + 1) % _order.Count;
            } while (!Current.IsAlive);

            return Current;
        }
    }
}
