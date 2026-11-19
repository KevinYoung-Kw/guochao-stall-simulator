using System;
using UnityEngine;

namespace Guochao
{
    // One read-only verdict for a single interaction target at a queried body
    // position. The very same evaluation drives highlight focus, primary-action
    // validation. Contact-time commitment rechecks spatial/business state and
    // facing; this query does not promise that future state cannot change.
    // Tier 0 = actionable now; 1 = spatially in range but not currently legal;
    // 2 = visible guidance only (near, not yet in range); 3 = out of reach.
    [Serializable]
    public struct InteractionCandidate
    {
        public const int TierActionable = 0;
        public const int TierInRangeButBlocked = 1;
        public const int TierGuidance = 2;
        public const int TierUnreachable = 3;

        public string target;
        public bool hasAnchor;
        public bool near;             // anchor 1.55 m + sight; ShoppingCart uses basket 1.10 m + sight
        public bool inRange;          // existing target-specific spatial/contact/envelope gate
        public bool primary;          // inRange + RunState.CanPrimary + supply-counter basket gate; caller also checks busy
        public int tier;
        public float distance;        // horizontal anchor distance; ShoppingCart uses its actual rim contact
        public float standDistance;   // horizontal body->anchor distance
        public float contactDistance; // horizontal body->contact distance (0 when no contact)
        public Vector3 anchor;
        public Vector3 contact;
        public string blockedBy;      // first failing gate, machine readable
    }
}
