using System.Collections.Generic;
using UnityEngine;

namespace BoxCutter
{
    /// <summary>
    /// The script implements a custom require logic that checks if the source box has at least one pair of required boxes attached to it.
    /// Great for realistic physics for tentacle rigs.
    /// </summary>
    public class BoxCutterCustomRequire : MonoBehaviour, IRequireLogic
    {
        public bool Run(BoxObj sourceBox, BoxObj[] attachedBoxes)
        {
            List<int> req = sourceBox.requiredBoxesInheritId;
            if (req.Count < 2) return false; // Need at least one pair
            if (attachedBoxes.Length < 2) return false; // Need at least two attachments to satisfy any pair

            // Build fast lookup of attached IDs
            HashSet<int> attachedIds = new HashSet<int>();
            for (int i = 0; i < attachedBoxes.Length; i++)
                attachedIds.Add(attachedBoxes[i].inheritableId);

            // Check pairs
            for (int i = 0; i + 1 < req.Count; i += 2)
            {
                int a = req[i];
                int b = req[i + 1];

                if (attachedIds.Contains(a) && attachedIds.Contains(b))
                    return true;
            }

            return false;
        }
    }
}