using System;
using System.Collections.Generic;
using System.Linq;

namespace Horse.MikuPhysicsLab
{
    internal static class LabCombinationCatalog
    {
        internal static ClipEntry[] Expand(ClipEntry[] clips)
        {
            var result = new List<ClipEntry>(clips);
            int Find(string key) => Array.FindIndex(clips, entry => entry.key == key);
            CombinationSegment Stage(int movement, int overlay = -1, float minimum = .8f)
            {
                if (movement < 0) throw new InvalidOperationException("Combination references a missing movement.");
                float duration = clips[movement].loop ? minimum : Math.Max(minimum, clips[movement].authoredDuration / clips[movement].speed);
                if (overlay >= 0) duration = Math.Max(duration, clips[overlay].authoredDuration / clips[overlay].speed);
                return new CombinationSegment { movement = movement, overlay = overlay, duration = duration + .15f };
            }
            void Add(string key, string label, IEnumerable<CombinationSegment> stages)
            {
                CombinationSegment[] sequence = stages.ToArray();
                var sources = sequence.SelectMany(stage => new[] { stage.movement, stage.overlay }).Where(i => i >= 0).Distinct().Select(i => clips[i]).ToArray();
                result.Add(new ClipEntry { key = key, label = label, category = "combination", rig = "body", speed = 1,
                    source = string.Join("; ", sources.Select(c => c.source)), segments = sequence,
                    matchedBindings = sources.Max(c => c.matchedBindings), missingPaths = sources.SelectMany(c => c.missingPaths ?? Array.Empty<string>()).Distinct().ToArray(),
                    inferredSettings = sources.Any(c => c.inferredSettings), baseKey = "idle" });
            }
            string[] route = { "idle", "walk", "run", "jump", "jumpLoop", "land", "run", "slide", "idle", "clamber", "airJump", "land", "idle" };
            Add("combo-locomotion", "Transitions / walk, run, jump, land, slide, clamber", route.Select(key => Stage(Find(key))));
            int flight = Array.FindIndex(clips, c => c.rig == "body" && c.flight);
            if (flight >= 0) Add("combo-flight", "Transitions / jump, flight, landing", new[] { Stage(Find("idle")), Stage(Find("jump")), Stage(flight), Stage(Find("land")), Stage(Find("idle")) });
            var groups = clips.Select((clip, index) => new { clip, index }).Where(x => x.clip.rig == "body" && x.clip.category == "weapon")
                .GroupBy(x => x.clip.source.Split(':')[0]);
            int weapon = 0;
            foreach (var group in groups)
            {
                var stages = new List<CombinationSegment>();
                foreach (string key in new[] { "idle", "walk", "run", "jump", "jumpLoop", "land", "slide", "idle" })
                {
                    var match = group.FirstOrDefault(x => x.clip.baseKey == key && !x.clip.source.EndsWith(":fire"));
                    stages.Add(Stage(Find(key), match?.index ?? -1));
                }
                foreach (var action in group.Where(x => new[] { ":fire", ":reload", ":equip", ":stow" }.Any(suffix => x.clip.source.EndsWith(suffix))))
                {
                    stages.Add(Stage(Find("run"), action.index));
                    stages.Add(Stage(Find("idle")));
                }
                Add("combo-weapon-" + weapon++, "Weapon transitions / " + group.Key, stages);
            }
            foreach (var emote in clips.Select((clip, index) => new { clip, index }).Where(x => x.clip.category == "emote" && x.clip.rig == "body"))
            {
                var stages = new List<CombinationSegment> { Stage(Find("idle")) };
                if (emote.clip.overlay)
                    foreach (string movement in new[] { "idle", "walk", "run" })
                    {
                        stages.Add(Stage(Find(movement), emote.index));
                        stages.Add(Stage(Find(movement)));
                    }
                else stages.Add(Stage(emote.index));
                stages.Add(Stage(Find("idle")));
                Add("combo-emote-" + emote.clip.key, "Emote transitions / " + emote.clip.label, stages);
            }
            return result.ToArray();
        }
    }
}
