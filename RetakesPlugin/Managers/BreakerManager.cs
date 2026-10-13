using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Utils;

using RetakesPlugin.Utils;

namespace RetakesPlugin.Managers;

public class BreakerManager
{
    // designerName -> action. A dictionary, not a list of tuples: Handle() used to run a linear
    // scan of this list for every entity on the map and re-read pEntity.DesignerName on each
    // comparison. See the comment in Handle() for why that mattered so much.
    private readonly Dictionary<string, string> _entityActions = new(StringComparer.Ordinal);

    private static readonly HashSet<string> MapsWithPropDynamic = [
        "de_vertigo",
        "de_nuke",
        "de_mirage"
    ];

    private static readonly HashSet<string> MapsWithFuncButton = ["de_nuke"];

    public BreakerManager(bool? shouldBreakBreakables, bool? shouldOpenDoors)
    {
        if (shouldBreakBreakables ?? false)
        {
            _entityActions["func_breakable"] = "Break";
            _entityActions["func_breakable_surf"] = "Break";
            _entityActions["prop.breakable.01"] = "Break";
            _entityActions["prop.breakable.02"] = "Break";

            if (MapsWithPropDynamic.Contains(Server.MapName))
            {
                _entityActions["prop_dynamic"] = "Break";
            }

            if (MapsWithFuncButton.Contains(Server.MapName))
            {
                _entityActions["func_button"] = "Kill";
            }

            Logger.LogInfo("BreakerManager", "Breakables enabled");
        }

        if (shouldOpenDoors ?? false)
        {
            _entityActions["prop_door_rotating"] = "open";
            Logger.LogInfo("BreakerManager", "Door opening enabled");
        }
    }

    public void Handle()
    {
        if (_entityActions.Count == 0)
        {
            return;
        }

        // Two passes on purpose. AcceptInput("Break"/"Kill") destroys the entity, and the old
        // single-pass loop then read pEntity.Next off that freed CEntityIdentity to continue
        // walking the active-entity list - a use-after-free that only shows up when the engine
        // actually reclaims the memory in the same frame, which is exactly what a round-restart
        // storm makes likely. Pass one only reads the list and records entity indices; pass two
        // re-resolves each index through the entity system and does the destroying.
        var targets = CollectTargets();

        var entitiesProcessed = 0;

        foreach (var (index, designerName, action) in targets)
        {
            CBaseEntity? target = designerName switch
            {
                "func_breakable" or "func_breakable_surf" or "prop_dynamic"
                    or "prop.breakable.01" or "prop.breakable.02"
                    => Utilities.GetEntityFromIndex<CBreakable>(index),

                "func_button" => Utilities.GetEntityFromIndex<CBaseButton>(index),

                "prop_door_rotating" => Utilities.GetEntityFromIndex<CPropDoorRotating>(index),

                _ => null
            };

            if (target == null || !target.IsValid)
            {
                continue;
            }

            // An index freed earlier in this pass can be handed straight back out to a new
            // entity (breaking a prop spawns gibs), so confirm the slot still holds what was
            // recorded before firing an input at it.
            if (target.DesignerName != designerName)
            {
                continue;
            }

            target.AcceptInput(action);
            entitiesProcessed++;
        }

        if (entitiesProcessed > 0)
        {
            Logger.LogDebug("BreakerManager", $"Processed {entitiesProcessed} entities");
        }
    }

    private List<(int Index, string DesignerName, string Action)> CollectTargets()
    {
        var targets = new List<(int Index, string DesignerName, string Action)>();

        var pEntity = new CEntityIdentity(EntitySystem.FirstActiveEntity);
        for (; pEntity != null && pEntity.Handle != IntPtr.Zero; pEntity = pEntity.Next)
        {
            // CEntityIdentity.DesignerName is NOT a cached field - every read is
            // Schema.GetUtf8String(), i.e. a schema offset lookup plus a native UTF-8 marshal
            // plus a fresh string allocation. The old loop read it once per _entityActions entry
            // (up to 6) for every entity on the map, and then a seventh time in the switch. A CS2
            // map has several thousand active entities, so a single round start did tens of
            // thousands of marshals and allocations - that is where the multi-hundred-millisecond
            // round-start frame came from. Read it once, then look the action up by hash.
            var designerName = pEntity.DesignerName;
            if (string.IsNullOrEmpty(designerName) || !_entityActions.TryGetValue(designerName, out var action))
            {
                continue;
            }

            var entity = new PointerTo<CEntityInstance>(pEntity.Handle).Value;
            if (entity == null || !entity.IsValid)
            {
                continue;
            }

            targets.Add(((int)entity.Index, designerName, action));
        }

        return targets;
    }
}
