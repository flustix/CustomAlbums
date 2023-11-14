﻿using System.Text.Json.Nodes;
using CustomAlbums.Data;
using CustomAlbums.Managers;
using CustomAlbums.Utilities;
using Il2CppAssets.Scripts.GameCore;
using Il2CppGameLogic;
using Il2CppPeroPeroGames.GlobalDefines;
using UnityEngine;
using Logger = CustomAlbums.Utilities.Logger;

namespace CustomAlbums
{
    internal static class BmsLoader
    {
        private static readonly Logger Logger = new(nameof(BmsLoader));
        private static int Delay = 0;

        /// <summary>
        /// Creates a Bms object from a BMS file.
        /// </summary>
        /// <param name="stream">MemoryStream of BMS file.</param>
        /// <param name="bmsName">Name of BMS score.</param>
        /// <returns>Loaded Bms object.</returns>
        internal static Bms Load(MemoryStream stream, string bmsName)
        {
            Logger.Msg($"Loading bms {bmsName}...");

            var bpmDict = new Dictionary<string, float>();
            var notePercents = new Dictionary<int, JsonObject>();
            var dataList = new List<JsonObject>();
            var notesArray = new JsonArray();
            var info = new JsonObject();

            var streamReader = new StreamReader(stream);
            while (streamReader.ReadLine()?.Trim() is { } line)
            {
                if (string.IsNullOrEmpty(line) || !line.StartsWith("#")) continue;

                // Remove # from beginning of line
                line = line[1..];

                if (line.Contains(' '))
                {
                    // Parse header
                    var split = line.Split(' ');
                    var key = split[0];
                    var value = split[1];

                    info[key] = value;

                    if (!key.Contains("BPM")) continue;

                    var bpmKey = string.IsNullOrEmpty(key[3..]) ? "00" : key[3..];
                    bpmDict.Add(bpmKey, float.Parse(value));

                    if (bpmKey != "00") continue;

                    var freq = 60f / float.Parse(value) * 4f;
                    var obj = new JsonObject
                    {
                        { "tick", 0f },
                        { "freq", freq }
                    };
                    dataList.Add(obj);
                }
                else if (line.Contains(':'))
                {
                    // Parse data field
                    var split = line.Split(':');
                    var key = split[0];
                    var value = split[1];

                    var beat = int.Parse(key[..3]);
                    var typeCode = key.Substring(3, 2);
                    var type = Bms.Channels[typeCode];

                    if (type == Bms.ChannelType.SpTimesig)
                    {
                        var obj = new JsonObject
                        {
                            { "beat", beat },
                            { "percent", float.Parse(value) }
                        };
                        notePercents.Add(beat, obj);
                    }
                    else
                    {
                        var objLength = value.Length / 2;
                        for (var i = 0; i < objLength; i++)
                        {
                            var note = value.Substring(i * 2, 2);
                            if (note == "00") continue;

                            var tick = (float)i / objLength + beat;

                            if (type is Bms.ChannelType.SpBpmDirect or Bms.ChannelType.SpBpmLookup)
                            {
                                // Handle BPM changes
                                var freqDivide = type == Bms.ChannelType.SpBpmLookup || bpmDict.ContainsKey(note)
                                    ? bpmDict[note]
                                    : Convert.ToInt32(note, 16);
                                var freq = 60f / (freqDivide * 4f);

                                var obj = new JsonObject
                                {
                                    { "tick", tick },
                                    { "freq", freq }
                                };
                                dataList.Add(obj);
                                dataList.Sort((l, r) =>
                                {
                                    var tickL = l["tick"].GetValue<float>();
                                    var tickR = r["tick"].GetValue<float>();

                                    return tickL.CompareTo(tickR);
                                });
                            }
                            else
                            {
                                // Parse other note data
                                var time = 0f; // num3
                                var totalOffset = 0f; // num4

                                var data = dataList.FindAll(d => d["tick"].GetValue<float>() < tick);
                                for (var j = data.Count - 1; j >= 0; j--)
                                {
                                    var obj = data[j];
                                    var offset = 0f; // num5
                                    var freq = obj["freq"].GetValue<float>(); // num6

                                    if (j - 1 >= 0)
                                    {
                                        var prevObj = data[j - 1];
                                        offset = prevObj["tick"].GetValue<float>() - obj["tick"].GetValue<float>();
                                    }

                                    if (j == 0) offset = tick - obj["tick"].GetValue<float>();

                                    var localOffset = totalOffset; // num7
                                    totalOffset += offset;
                                    var floorOffset = Mathf.FloorToInt(localOffset); // num8
                                    var ceilOffset = Mathf.CeilToInt(totalOffset); // num9

                                    for (var k = floorOffset; k < ceilOffset; k++)
                                    {
                                        var off = 1f; // num10

                                        if (k == floorOffset)
                                            off = k + 1 - localOffset;
                                        else if (k == ceilOffset - 1)
                                            off = totalOffset - (ceilOffset - 1);
                                        else if (ceilOffset == floorOffset + 1)
                                            off = totalOffset - localOffset;

                                        notePercents.TryGetValue(k, out var node);
                                        var percent = node?["percent"].GetValue<float>() ?? 1f;
                                        time += Mathf.RoundToInt(off * percent * freq / 1E-06f) * 1E-06F;
                                    }
                                }

                                //Logger.Msg("Time when setting obj: " + time);
                                var noteObj = new JsonObject
                                {
                                    { "time", time },
                                    { "value", note },
                                    { "tone", typeCode }
                                };
                                notesArray.Add(noteObj);
                            }
                        }
                    }
                }
            }

            var list = notesArray.ToList();
            list.Sort((l, r) =>
            {
                var lTime = l["time"]!.GetValue<float>();
                var rTime = r["time"]!.GetValue<float>();
                var lTone = l["tone"]!.GetValue<string>();
                var rTone = r["tone"]!.GetValue<string>();

                // Accurate for note sorting up to 6 decimal places
                var lScore = (long)(lTime * 1000000) * 10 + (lTone == "15" ? 0 : 1);
                var rScore = (long)(rTime * 1000000) * 10 + (rTone == "15" ? 0 : 1);

                return Math.Sign(lScore - rScore);
            });

            notesArray.Clear();
            list.ForEach(notesArray.Add);

            var percentsArray = new JsonArray();
            notePercents.Values.ToList().ForEach(percentsArray.Add);
            var bms = new Bms
            {
                Info = info,
                Notes = notesArray,
                NotesPercent = percentsArray,
                Md5 = stream.GetHash()
            };
            bms.Info["NAME"] = bmsName;
            bms.Info["NEW"] = true;

            if (bms.Info.TryGetPropertyValue("BANNER", out var banner))
                bms.Info["BANNER"] = "cover/" + banner;
            else
                bms.Info["BANNER"] = "cover/none_cover.png";

            Logger.Msg($"Loaded bms {bmsName}.");

            return bms;
        }

        /// <summary>
        /// Transmutes Bms data into StageInfo data.
        /// </summary>
        /// <param name="bms">The Bms object to transmute.</param>
        /// <returns>The transmuted StageInfo object.</returns>
        internal static StageInfo TransmuteData(Bms bms)
        {
            if (Bms.NoteData.Count == 0) Bms.InitNoteData();
            MusicDataManager.Clear();
            Delay = 0;

            var stageInfo = ScriptableObject.CreateInstance<StageInfo>();

            var noteData = GetNoteData(bms);
            Logger.Msg("Got note data");

            LoadMusicData(noteData);
            // TODO: Process boss animations
            // TODO: Process geminis
            // TODO: Process note delay

            return stageInfo;
        }

        private static JsonArray GetNoteData(Bms bms)
        {
            var processed = new JsonArray();

            var speedAir = int.Parse(bms.Info["PLAYER"]?.GetValue<string>() ?? "0");
            var speedGround = speedAir;

            var objectId = 1;
            for (var i = 0; i < bms.Notes.Count; i++)
            {
                var note = bms.Notes[i];
                if (note is null) continue;

                var bmsKey = note["value"]?.GetValue<string>() ?? "00";
                var bmsId = Bms.BmsIds.TryGetValue(bmsKey, out var bType) ? bType : Bms.BmsId.None;
                var channel = note["tone"]?.GetValue<string>() ?? string.Empty;
                var channelType = Bms.Channels.TryGetValue(channel, out var cType) ? cType : Bms.ChannelType.None;

                // Handle lane type
                var pathway = -1;
                if (channelType.HasFlag(Bms.ChannelType.Air))
                    pathway = 1;
                else if (channelType.HasFlag(Bms.ChannelType.Ground) || channelType.HasFlag(Bms.ChannelType.Event))
                    pathway = 0;
                if (pathway == -1) continue;

                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                // ReSharper disable once SwitchStatementMissingSomeEnumCasesNoDefault
                switch (bmsId)
                {
                    // Handle speed changes
                    case Bms.BmsId.Speed1Both:
                        speedGround = 1;
                        speedAir = 1;
                        break;
                    case Bms.BmsId.Speed2Both:
                        speedGround = 2;
                        speedAir = 2;
                        break;
                    case Bms.BmsId.Speed3Both:
                        speedGround = 3;
                        speedAir = 3;
                        break;
                    case Bms.BmsId.Speed1Low:
                        speedGround = 1;
                        break;
                    case Bms.BmsId.Speed1High:
                        speedAir = 1;
                        break;
                    case Bms.BmsId.Speed2Low:
                        speedGround = 2;
                        break;
                    case Bms.BmsId.Speed2High:
                        speedAir = 2;
                        break;
                    case Bms.BmsId.Speed3Low:
                        speedGround = 3;
                        break;
                    case Bms.BmsId.Speed3High:
                        speedAir = 3;
                        break;
                }

                var speed = (pathway == 1) ? speedAir : speedGround;
                var scene = bms.Info["GENRE"]?.GetValue<string>();
                if (!Bms.NoteData.TryGetValue(Bms.GetNoteDataKey(bmsKey, pathway, speed, scene), out var configData))
                    continue;

                var time = note["time"]?.GetValueAsDecimal() ?? 0M;

                // Hold note & masher 
                var holdLength = 0M;
                var isHold = configData.GetNoteType() is NoteType.Press or NoteType.Mul;
                if (isHold)
                {
                    if (channelType.HasFlag(Bms.ChannelType.SpTapHolds))
                    {
                        holdLength = 0.001M;
                    }
                    else
                    {
                        for (var j = i + 1; j < bms.Notes.Count; j++)
                        {
                            var holdEndNote = bms.Notes[j];
                            var holdEndTime = holdEndNote?["time"]?.GetValueAsDecimal() ?? 0M;
                            var holdEndBmsKey = holdEndNote?["value"]?.GetValue<string>() ?? string.Empty;
                            var holdEndChannel = holdEndNote?["tone"]?.GetValue<string>() ?? string.Empty;

                            if (holdEndBmsKey != bmsKey || holdEndChannel != channel) continue;
                            holdLength = holdEndTime - time;
                            bms.Notes[j]!["value"] = "";
                            break;
                        }
                    }
                }

                processed.Add(new JsonObject
                {
                    ["id"] = objectId++,
                    ["time"] = time,
                    ["note_uid"] = configData.uid,
                    ["length"] = holdLength,
                    ["pathway"] = pathway,
                    ["blood"] = !isHold && channelType.HasFlag(Bms.ChannelType.SpBlood)
                });
            }

            return processed;
        }

        private static void LoadMusicData(JsonArray noteData)
        {
            short noteId = 1;
            foreach (var node in noteData)
            {
                if (noteId == short.MaxValue)
                {
                    Logger.Warning($"Cannot process full chart, there are too many objects. Max objects is {short.MaxValue}.");
                    break;
                }

                var configData = node.ToNoteConfigData();
                if (configData.time < 0) continue;
                if (!Bms.NoteData.TryGetValue(configData.note_uid, out var newNoteData))
                    newNoteData = Interop.CreateTypeValue<NoteConfigData>();

                // Create a new note for each configData
                var newNote = Interop.CreateTypeValue<MusicData>();
                newNote.objId = noteId++;
                newNote.tick = Il2CppSystem.Decimal.Round(configData.time, 3);
                newNote.configData = configData;
                newNote.isLongPressEnd = false;
                newNote.isLongPressing = false;
                newNote.noteData = newNoteData;

                MusicDataManager.Add(newNote);

                // Create ticks for hold notes. If it isn't a hold note, there is no need to continue.
                if (!newNote.isLongPressStart) continue;

                // Calculate the index in which the hold note ends
                var endIndex = (int)(Il2CppSystem.Decimal.Round(
                    newNote.tick + configData.length - newNoteData.left_great_range - newNoteData.left_perfect_range,
                    3) / (Il2CppSystem.Decimal)0.001f);

                for (var i = 0; i < newNote.longPressCount; i++)
                {
                    var holdTick = Interop.CreateTypeValue<MusicData>();
                    holdTick.objId = noteId++;
                    holdTick.tick = newNote.tick + i == newNote.longPressCount ? configData.length : (Il2CppSystem.Decimal)0.1f * i;
                    holdTick.noteData = newNoteData;
                    holdTick.longPressPTick = configData.time;
                    holdTick.endIndex = endIndex;
                    holdTick.isLongPressing = i != newNote.longPressCount;
                    holdTick.isLongPressEnd = i == newNote.longPressCount;
                    holdTick.configData = configData;
                    holdTick.configData.length = 0;

                    MusicDataManager.Add(holdTick);
                }
            }

            Logger.Msg("Loaded music data!");
        }
    }
}