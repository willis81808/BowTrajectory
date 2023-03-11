using System.Linq;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using Mache;
using BepInEx.Unity.IL2CPP;
using UnityEngine;
using Mache.Utils;
using Sons.Weapon;
using TheForest.Utils;
using static Ballistics.BulletHandler;
using Mache.UI;
using BepInEx.Configuration;

namespace BowTrajectory
{
    [BepInDependency("com.willis.sotf.mache")]
    [BepInPlugin(ModId, ModName, Version)]
    [BepInProcess("SonsOfTheForest.exe")]
    public class Plugin : BasePlugin
    {
        public const string ModId = "com.willis.sotf.bowtrajectory";
        public const string ModName = "Bow Trajectory";
        public const string Version = "1.0.0";

        internal static Plugin Instance { get; private set; }

        public override void Load()
        {
            Instance = this;
            AddComponent<TrajectoryManager>();
        }
    }

    public class TrajectoryManager : MonoBehaviour
    {
        private ConfigEntry<bool> craftedBowEnabled, tacticalBowEnabled, crossbowEnabled;
        private ConfigEntry<float> lineStartWidth, lineEndWidth;

        private LineRenderer _trajectoryLine;
        private LineRenderer TrajectoryLine
        {
            get
            {
                if (_trajectoryLine == null)
                {
                    var pluginDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    var bundlePath = Path.Combine(pluginDir, "StreamingAssets", "willis", "bowtrajectory");
                    var bundle = UniverseLib.AssetBundle.LoadFromFile(bundlePath);
                    _trajectoryLine = Instantiate(bundle.LoadAsset<GameObject>("Trajectory Line")).GetComponent<LineRenderer>();
                    DontDestroyOnLoad(_trajectoryLine.gameObject);
                    _trajectoryLine.gameObject.SetActive(false);
                }
                return _trajectoryLine;
            }
        }

        private bool IsLineInitialized
        {
            get => _trajectoryLine != null;
        }

        private void Awake()
        {
            craftedBowEnabled = Plugin.Instance.Config.Bind(Plugin.ModId, "CraftedBowEnabled", true, "Trajectory enabled for Crafted Bow?");
            tacticalBowEnabled = Plugin.Instance.Config.Bind(Plugin.ModId, "TacticalBowEnabled", true, "Trajectory enabled for Tactical Bow?");
            crossbowEnabled = Plugin.Instance.Config.Bind(Plugin.ModId, "CrossbowEnabled", true, "Trajectory enabled for Crossbow?");

            lineStartWidth = Plugin.Instance.Config.Bind(Plugin.ModId, "TrajectoryLineStartWidth", 0.1f, "Trajectory line starting width");
            lineEndWidth = Plugin.Instance.Config.Bind(Plugin.ModId, "TrajectoryLineEndWidth", 0.1f, "Trajectory line ending width");
        }

        private void Start()
        {
            Mache.Mache.RegisterMod(() => new ModDetails
            {
                Id = Plugin.ModId,
                Name = Plugin.ModName,
                Description = "Renders a line when drawing your bow indicating the expected trajectory of your arrows.",
                OnFinishedCreating = CreateMenu
            });
        }

        private void CreateMenu(GameObject parent)
        {
            var startWidthSlider = new SliderComponent
            {
                Name = "Line Starting Width",
                MinValue = 0f,
                MaxValue = 1f,
                StartValue = lineStartWidth.Value,
                OnValueChanged = (self, val) =>
                {
                    lineStartWidth.Value = val;
                    UpdateLineWidth();
                }
            };
            var endWidthSlider = new SliderComponent
            {
                Name = "Line Ending Width",
                MinValue = 0.1f,
                MaxValue = 1f,
                StartValue = lineEndWidth.Value,
                OnValueChanged = (self, val) =>
                {
                    lineEndWidth.Value = val;
                    UpdateLineWidth();
                }
            };
            var craftedBowToggle = new ToggleComponent
            {
                Title = "Crafted Bow Trajectory Enabled?",
                OnValueChanged = (self, val) => craftedBowEnabled.Value = val
            };
            var tacticalBowToggle = new ToggleComponent
            {
                Title = "Tactical Bow Trajectory Enabled?",
                OnValueChanged = (self, val) => tacticalBowEnabled.Value = val
            };
            var crossbowToggle = new ToggleComponent
            {
                Title = "Crossbow Trajectory Enabled?",
                OnValueChanged = (self, val) => crossbowEnabled.Value = val
            };
            var resetDefaultsButton = new ButtonComponent
            {
                Text = "Reset Defaults",
                OnClick = (self) =>
                {
                    startWidthSlider.SliderObject.Set((float)lineStartWidth.DefaultValue);
                    endWidthSlider.SliderObject.Set((float)lineEndWidth.DefaultValue);

                    craftedBowToggle.ToggleObject.Set((bool)craftedBowEnabled.DefaultValue);
                    tacticalBowToggle.ToggleObject.Set((bool)tacticalBowEnabled.DefaultValue);
                    crossbowToggle.ToggleObject.Set((bool)crossbowEnabled.DefaultValue);
                }
            };

            MenuPanel.Builder()
                .AddComponent(startWidthSlider)
                .AddComponent(endWidthSlider)
                .AddComponent(craftedBowToggle)
                .AddComponent(tacticalBowToggle)
                .AddComponent(crossbowToggle)
                .AddComponent(resetDefaultsButton)
                .BuildToTarget(parent);

            var buttonColors = resetDefaultsButton.ButtonObject.Component.colors;
            buttonColors.highlightedColor = Color.red;
            resetDefaultsButton.ButtonObject.Component.colors = buttonColors;
        }

        private void FixedUpdate()
        {
            if (!LocalPlayer.IsInWorld || LocalPlayer.Inventory.RightHandItem == null)
            {
                if (IsLineInitialized)
                {
                    TrajectoryLine.gameObject.SetActive(false);
                }
                return;
            }

            var item = LocalPlayer.Inventory.RightHandItem;
            var ranged = item.ItemObject.GetComponentInChildren<RangedWeapon>();
            var controller = item.ItemObject.GetComponentInChildren<BowWeaponController>();

            if (ranged == null || controller == null || !EnabledForItem(item._itemID))
            {
                TrajectoryLine.gameObject.SetActive(false);
                return;
            }

            if (controller._attackState == RangedWeaponController.AttackState.MidAttack)
            {
                TrajectoryLine.gameObject.SetActive(true);
                UpdateLineWidth();

                ranged._simulatedBulletInfo = ranged.GetAmmo()._properties.ProjectileInfo;
                ranged.SimulateTrajectory();
                TrajectoryData trajectory = ranged.SimulateTrajectory(ranged.physicalSpawnPoint.position, ranged.physicalSpawnPoint.forward);
                TrajectoryLine.SetVertexCount(trajectory.arcPoints.Count);
                for (int i = 0; i < trajectory.arcPoints.Count; i++)
                {
                    TrajectoryLine.SetPosition(i, trajectory.arcPoints[i]);
                }
            }
            else
            {
                TrajectoryLine.gameObject.SetActive(false);
            }
        }

        private void UpdateLineWidth()
        {
            if (!IsLineInitialized) return;
            TrajectoryLine.startWidth = lineStartWidth.Value;
            TrajectoryLine.endWidth = lineEndWidth.Value;
        }

        private bool EnabledForItem(int itemID)
        {
            if (itemID == GameItem.CraftedBow.GetId() && craftedBowEnabled.Value)
            {
                return true;
            }
            else if (itemID == GameItem.TacticalBow.GetId() && tacticalBowEnabled.Value)
            {
                return true;
            }
            else if (itemID == GameItem.Crossbow.GetId() && crossbowEnabled.Value)
            {
                return true;
            }
            return false;
        }
    }
}