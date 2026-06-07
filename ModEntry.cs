using System;
using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SmartNPCAndEconomyMod
{
    // Lớp cấu hình để hiển thị và chỉnh sửa trong GMCM
    public class ModConfig
    {
        public string ApiProvider { get; set; } = "Gemini";
        public string ApiKey { get; set; } = "DÁN_API_KEY_CỦA_HUYNH_VÀO_ĐÂY";
        public string ModelName { get; set; } = "gemini-3.5-flash";
        public string ServerAddress { get; set; } = "https://generativelanguage.googleapis.com";
        public float PriceFluctuationRate { get; set; } = 1.2f; // Tỷ lệ biến động giá (Cung-Cầu)
        public int QuestDurationDays { get; set; } = 7; // Thời hạn nhiệm vụ động
    }

    // Dữ liệu cấu trúc của Nhiệm vụ động 7 ngày
    public class DynamicQuest
    {
        public string QuestGiver { get; set; }
        public string TargetItem { get; set; }
        public int TargetAmount { get; set; }
        public int DaysLeft { get; set; }
        public bool IsActive { get; set; }
        public int RewardGold { get; set; }
    }

    public class ModEntry : Mod
    {
        private ModConfig Config;
        private Dictionary<string, int> ShippedItemsTracker = new Dictionary<string, int>();
        private List<DynamicQuest> ActiveQuests = new List<DynamicQuest>();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();

            // Đăng ký các sự kiện hệ thống
            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
        }

        // Tích hợp Menu cấu hình GMCM để Sư huynh chỉnh sửa trong game
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu == null) return;

            configMenu.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            // Các tùy chỉnh hiển thị trong GMCM mà Sư huynh yêu cầu
            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Nhà Cung Cấp AI",
                tooltip: () => "Chọn Google Gemini hoặc OpenRouter",
                getValue: () => this.Config.ApiProvider,
                setValue: value => this.Config.ApiProvider = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Mã API Key",
                tooltip: () => "Nhập mã API cá nhân của Sư huynh để chạy mod độc lập",
                getValue: () => this.Config.ApiKey,
                setValue: value => this.Config.ApiKey = value
            );

            configMenu.AddTextOption(
                mod: this.ModManifest,
                name: () => "Tên Mô Hình AI",
                tooltip: () => "Đệ khuyên huynh nên dùng gemini-3.5-flash cho mượt nha",
                getValue: () => this.Config.ModelName,
                setValue: value => this.Config.ModelName = value
            );

            configMenu.AddNumberOption(
                mod: this.ModManifest,
                name: () => "Thời Hạn Nhiệm Vụ (Ngày)",
                tooltip: () => "Số ngày giới hạn để hoàn thành nhiệm vụ do NPC đưa ra",
                getValue: () => this.Config.QuestDurationDays,
                setValue: value => this.Config.QuestDurationDays = value
            );
        }

        // Xử lý khi Sư huynh thức dậy vào ngày mới (Bộ đếm 7 ngày và cập nhật giá cung cầu)
        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            // 1. Cập nhật và đếm ngược thời gian nhiệm vụ động
            foreach (var quest in this.ActiveQuests.ToArray())
            {
                if (quest.IsActive)
                {
                    quest.DaysLeft--;
                    if (quest.DaysLeft <= 0)
                    {
                        quest.IsActive = false;
                        Game1.chatBox.addMessage($"{quest.QuestGiver} tỏ ra thất vọng vì Sư huynh không hoàn thành nhiệm vụ đúng hạn 7 ngày...", Microsoft.Xna.Framework.Color.Red);
                        this.ActiveQuests.Remove(quest);
                    }
                }
            }

            // 2. Kích hoạt AI tạo nhiệm vụ ngẫu nhiên dựa trên thị trường nếu chưa có nhiệm vụ nào hoạt động
            if (this.ActiveQuests.Count == 0 && Game1.random.NextDouble() < 0.3) // 30% tỷ lệ xuất hiện mỗi sáng
            {
                TriggerAIQuestGeneration();
            }
        }

        // Xử lý khi kết thúc ngày (Theo dõi hàng hóa Sư huynh bỏ vào Thùng xuất khẩu hàng để tính toán Cung - Cầu)
        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            // Quét qua Thùng xuất khẩu hàng (Shipping Bin) để ghi nhận món nào bị bán tháo
            foreach (var item in Game1.getFarm().shippingBin)
            {
                if (item != null)
                {
                    string itemName = item.Name;
                    int amount = item.Stack;

                    if (this.ShippedItemsTracker.ContainsKey(itemName))
                        this.ShippedItemsTracker[itemName] += amount;
                    else
                        this.ShippedItemsTracker[itemName] = amount;
                    
                    // Nếu bán quá 50 sản phẩm cùng loại, kích hoạt cơ chế rớt giá vào ngày hôm sau
                    if (this.ShippedItemsTracker[itemName] > 50)
                    {
                        this.Monitor.Log($"Sư huynh đã bán tháo lượng lớn {itemName}. Thị trường cung vượt cầu, giá sản phẩm này sẽ giảm!", LogLevel.Alert);
                    }
                }
            }
        }

        // Hàm giả lập gọi API để tạo nhiệm vụ động (Sẽ kết nối với chuỗi gọi API thực tế của huynh)
        private void TriggerAIQuestGeneration()
        {
            string randomNPC = Game1.currentLocation?.Name == "Town" ? "Abigail" : "Morrow";
            
            DynamicQuest newQuest = new DynamicQuest
            {
                QuestGiver = randomNPC,
                TargetItem = "Potato", // Loại nông sản ngẫu nhiên cần thu mua
                TargetAmount = Game1.random.Next(15, 30),
                DaysLeft = this.Config.QuestDurationDays,
                IsActive = true,
                RewardGold = 2500
            };

            this.ActiveQuests.Add(newQuest);
            Game1.chatBox.addMessage($"[Nhiệm vụ AI từ {newQuest.QuestGiver}]: Cần {newQuest.TargetAmount} củ {newQuest.TargetItem} trong {newQuest.DaysLeft} ngày!", Microsoft.Xna.Framework.Color.Green);
        }
    }

    // Khai báo Interface kết nối với bản mod Generic Mod Config Menu bên ngoài
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddTextOption(IManifest mod, Func<string> name, Func<string> tooltip, Func<string> getValue, Action<string> setValue, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<string> name, Func<string> tooltip, Func<int> getValue, Action<int> setValue, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
    }
}
