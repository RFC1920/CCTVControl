//#define DEBUG
using System;
using System.Collections.Generic;
using UnityEngine;
using Oxide.Core;
using System.Text;
using System.Linq;
using Oxide.Core.Plugins;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Game.Rust.Cui;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("CCTVControl", "RFC1920", "1.0.0")]
    [Description("Oxide Plugin")]
    class CCTVControl : RustPlugin
    {
        #region vars
        [PluginReference]
        private Plugin Clans, Friends, RustIO;

        private const string permCCTV = "cctvcontrol.use";
        private const string permCCTVList = "cctvcontrol.admin";
        float userRange;
        float adminRange;
        bool useFriends = false;
        bool useClans = false;
        bool useTeams = false;
        bool userMapWide = false;
        #endregion

        #region Message
        private string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);
        private void Message(IPlayer player, string key, params object[] args) => player.Reply(Lang(key, player.Id, args));
        #endregion

        #region init
        void Init()
        {
            AddCovalenceCommand("cctv", "cmdCCTV");
            AddCovalenceCommand("cctvlist", "cmdCCTVList");

            permission.RegisterPermission(permCCTV, this);
            permission.RegisterPermission(permCCTVList, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["notauthorized"] = "You are not authorized to use this command!",
                ["foundCamera"] = "Found Camera {0} owned by {1}",
                ["foundCameras"] = "Found Cameras:",
                ["cameraexists"] = "Camera {0} already in station",
                ["ownedby"] = " owned by ",
                ["foundStation"] = "Found ComputerStation...",
                ["noStation"] = "No ComputerStation found...",
                ["helptext1"] = "CCTV Control Instructions:",
                ["helptext2"] = "  type /cctv to add your local cameras",
            }, this);
        }

        void Loaded()
        {
            LoadVariables();
        }
        #endregion

        #region Config
        protected override void LoadDefaultConfig()
        {
#if DEBUG
            Puts("Creating a new config file...");
#endif
            userRange = 200f;
            userMapWide = false;
            adminRange = 4000f;
            useFriends = false;
            useClans = false;
            useTeams = false;
            LoadVariables();
        }

        private void LoadConfigVariables()
        {
            CheckCfgFloat("userRange", ref userRange);
            CheckCfgFloat("adminRange", ref adminRange);
            CheckCfg<bool>("useFriends", ref useFriends);
            CheckCfg<bool>("useClans", ref useClans);
            CheckCfg<bool>("useTeams", ref useTeams);
            CheckCfg<bool>("userMapWide", ref userMapWide);
        }

        private void LoadVariables()
        {
            LoadConfigVariables();
            SaveConfig();
        }

        private void CheckCfg<T>(string Key, ref T var)
        {
            if(Config[Key] is T)
            {
                var = (T)Config[Key];
            }
            else
            {
                Config[Key] = var;
            }
        }

        private void CheckCfgFloat(string Key, ref float var)
        {
            if(Config[Key] != null)
            {
                var = Convert.ToSingle(Config[Key]);
            }
            else
            {
                Config[Key] = var;
            }
        }

        object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if(data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                //Changed = true;
            }

            object value;
            if(!data.TryGetValue(datavalue, out value))
            {
                value = defaultValue;
                data[datavalue] = value;
                //Changed = true;
            }
            return value;
        }
        #endregion

        #region Main
        [Command("cctv")]
        void cmdCCTV(IPlayer iplayer, string command, string[] args)
        {
            if(!iplayer.HasPermission(permCCTV)) { Message(iplayer, "notauthorized"); return; }
            var player = iplayer.Object as BasePlayer;
            List<ComputerStation> stations = new List<ComputerStation>();
            Vis.Entities<ComputerStation>(player.transform.position, 3f, stations);
            bool foundS = false;
            bool foundC = false;
            foreach(var station in stations)
            {
                foundS = true;
                Message(iplayer, "foundStation");
                List<CCTV_RC> cameras = new List<CCTV_RC>();

                float range = userRange;
                if(userMapWide) range = adminRange;

                Vis.Entities<CCTV_RC>(player.transform.position, range, cameras);
                List<string> foundCameras = new List<string>();
                foreach(var camera in cameras)
                {
                    var realcam = camera as IRemoteControllable;
                    var ent = realcam.GetEnt();
                    var cname = realcam.GetIdentifier();
                    if(foundCameras.Contains(cname)) continue;

                    if(ent.OwnerID.ToString() == iplayer.Id || IsFriend(player.userID, ent.OwnerID))
                    {
                        foundCameras.Add(cname);
                        if(station.controlBookmarks.ContainsKey(cname))
                        {
                            Message(iplayer, "cameraexists", cname);
                            continue;
                        }

						var pl = BasePlayer.Find(ent.OwnerID.ToString());
                        Message(iplayer, "foundCamera", cname, pl.displayName);
                        AddCamera(player, station, cname);
                    }
                }
                break;
            }
            if(!foundS)
            {
                Message(iplayer, "noStation");
            }
        }

        [Command("cctvlist")]
        void cmdCCTVList(IPlayer iplayer, string command, string[] args)
        {
            if(!iplayer.IsAdmin || !iplayer.HasPermission(permCCTVList)) return;
            var player = iplayer.Object as BasePlayer;

            List<CCTV_RC> cameras = new List<CCTV_RC>();
            Vis.Entities<CCTV_RC>(player.transform.position, adminRange, cameras);
            List<string> foundCameras = new List<string>();
            string msg = null;

            Message(iplayer, "foundCameras");
            foreach(var camera in cameras)
            {
                var realcam = camera as IRemoteControllable;
                var loc = realcam.GetEyes();
                var ent = realcam.GetEnt();
                var cname = realcam.GetIdentifier();
                if(foundCameras.Contains(cname)) continue;
                foundCameras.Add(cname);
                msg += cname + " @ " + loc.position.ToString() + Lang("ownedby") + ent.OwnerID.ToString() + "\n";
            }
            Message(iplayer, msg);
        }

        void AddCamera(BasePlayer basePlayer, ComputerStation station, string str)
        {
            uint d = 0;
            BaseNetworkable baseNetworkable;
            IRemoteControllable component;
            bool flag = false;
            foreach (IRemoteControllable allControllable in RemoteControlEntity.allControllables)
            {
                if (allControllable == null || !(allControllable.GetIdentifier() == str))
                {
                    continue;
                }
                if (allControllable.GetEnt() != null)
                {
                    d = allControllable.GetEnt().net.ID;
                    flag = true;
                    if (!flag)
                    {
                        return;
                    }
                    baseNetworkable = BaseNetworkable.serverEntities.Find(d);
                    if (baseNetworkable == null)
                    {
                        return;
                    }
                    component = baseNetworkable.GetComponent<IRemoteControllable>();
                    if (component == null)
                    {
                        return;
                    }
                    if (str == component.GetIdentifier())
                    {
                        station.controlBookmarks.Add(str, d);
                    }
                    station.SendControlBookmarks(basePlayer);
                    return;
                }
                else
                {
                    Debug.LogWarning("Computer station added bookmark with missing ent, likely a static CCTV (wipe the server)");
                }
            }
        }

        // playerid = active player, ownerid = owner of camera, who may be offline
        bool IsFriend(ulong playerid, ulong ownerid)
        {
            if(useFriends && Friends != null)
            {
                var fr = Friends?.CallHook("AreFriends", playerid, ownerid);
                if(fr != null && (bool)fr)
                {
                    return true;
                }
            }
            if(useClans && Clans != null)
            {
                string playerclan = (string)Clans?.CallHook("GetClanOf", playerid);
                string ownerclan  = (string)Clans?.CallHook("GetClanOf", ownerid);
                if(playerclan == ownerclan && playerclan != null && ownerclan != null)
                {
                    return true;
                }
            }
            if(useTeams)
            {
                BasePlayer player = BasePlayer.FindByID(playerid);
                if(player.currentTeam != (long)0)
                {
                    RelationshipManager.PlayerTeam playerTeam = RelationshipManager.Instance.FindTeam(player.currentTeam);
                    if(playerTeam == null) return false;
                    if(playerTeam.members.Contains(ownerid))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

//        private void OnPlayerInput(BasePlayer player, InputState input)
//        {
//            if(player == null || input == null) return;
//            try
//            {
//                var activeCamera = player.GetMounted().GetComponentInParent<ComputerStation>() ?? null;
//                if(activeCamera != null)
//                {
//                    activeCamera.currentlyControllingEnt.Get(true).GetComponent<IRemoteControllable>().UserInput(input, player);
//                }
//            }
//            catch {}
//        }

        // How they accept input from the user to a camera
//        public override void UserInput(InputState inputState, BasePlayer player)
//        {
//            if (!this.hasPTZ)
//            {
//                return;
//            }
//            float single = 1f;
//            float single1 = Mathf.Clamp(-inputState.current.mouseDelta.y, -1f, 1f);
//            float single2 = Mathf.Clamp(inputState.current.mouseDelta.x, -1f, 1f);
//            this.pitchAmount = Mathf.Clamp(this.pitchAmount + single1 * single * this.turnSpeed, this.pitchClamp.x, this.pitchClamp.y);
//            this.yawAmount = Mathf.Clamp(this.yawAmount + single2 * single * this.turnSpeed, this.yawClamp.x, this.yawClamp.y);
//            Quaternion quaternion = Quaternion.Euler(this.pitchAmount, 0f, 0f);
//            Quaternion quaternion1 = Quaternion.Euler(0f, this.yawAmount, 0f);
//            this.pitch.transform.localRotation = quaternion;
//            this.yaw.transform.localRotation = quaternion1;
//            if (single1 != 0f || single2 != 0f)
//            {
//                base.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
//            }
//        }
        #endregion
    }
}