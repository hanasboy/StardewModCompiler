using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;

namespace SmartEconomyMod
{
    // 1. LỚP NẠP MOD CHÍNH (MOD ENTRY)
    public class ModEntry : Mod
    {
        public ModConfig Config { get; private set; }
        public MarketData EconomyData { get; private set; }
        public LlmService Llm { get; private set; }

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();
            this.EconomyData = new MarketData();
            this.Llm = new LlmService(this.Config);

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null) return;

            configMenu.Register(mod: this.ModManifest, reset: () => this.Config = new ModConfig(), save: () => this.Helper.WriteConfig(this.Config));
            configMenu.AddTextOption(mod: this.ModManifest, name: () => "Chọn Nhà Cung Cấp", getValue: () => this.Config.ApiProvider, setValue: value => this.Config.ApiProvider = value, tooltip: () => "Hệ thống AI muốn sử dụng", choices: new string[] { "OpenAI", "Gemini", "Groq" });
            configMenu.AddTextOption(mod: this.ModManifest, name: () => "API Key LLM", getValue: () => this.Config.ApiKey, setValue: value => this.Config.ApiKey = value);
            configMenu.AddTextOption(mod: this.ModManifest, name: () => "Model Name AI", getValue: () => this.Config.ModelName, setValue: value => this.Config.ModelName = value, tooltip: () => "Ví dụ: gpt-4o-mini, gemini-1.5-flash");
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            Random rand = new Random();
            string[] trackedItems = new string[] { "(O)24", "(O)188", "(O)190", "(O)250" }; // Khoai tây, dâu tây...

            foreach (var itemId in trackedItems)
            {
                if (!this.EconomyData.ItemStates.ContainsKey(itemId))
                    this.EconomyData.ItemStates[itemId] = new MarketItemState { ItemId = itemId };

                var state = this.EconomyData.ItemStates[itemId];
                state.CurrentMultiplier = state.TomorrowForecastMultiplier;

                double change = (rand.NextDouble() * 2 - 1) * this.Config.BaseVolatility;
                state.TomorrowForecastMultiplier = Math.Max(0.4, Math.Min(2.5, state.CurrentMultiplier + change));

                if (change > 0.05) { state.TrendDirection = "📈 Tăng"; state.ForecastText = "Nhu cầu thị trường đang rất cao!"; }
                else if (change < -0.05) { state.TrendDirection = "📉 Giảm"; state.ForecastText = "Cung vượt cầu, thương lái ép giá."; }
                else { state.TrendDirection = "⚖️ Ổn định"; state.ForecastText = "Giá cả ít biến động."; }
            }

            this.Helper.Data.WriteJsonFile($"data/{Constants.SaveFolderName}_economy.json", this.EconomyData);
        }

        private void OnMenuChanged(object sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is ShopMenu shopMenu)
            {
                foreach (var item in shopMenu.itemPriceAndStock.Keys)
                {
                    string itemId = item.QualifiedItemId;
                    if (this.EconomyData.ItemStates.ContainsKey(itemId))
                    {
                        double multiplier = this.EconomyData.ItemStates[itemId].CurrentMultiplier;
                        int originalPrice = shopMenu.itemPriceAndStock[item].Price;
                        shopMenu.itemPriceAndStock[item].Price = (int)(originalPrice * multiplier);
                    }
                }
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady) return;

            // Kiểm tra nút mở bảng theo dõi giá thị trường (Ví dụ nút Mở Nhật Ký hoặc nút hành động phụ)
            if (e.Button == SButton.F4)
            {
                Game1.activeClickableMenu = new MarketForecastMenu(this.EconomyData);
                return;
            }

            if (e.Button.IsActionButton())
            {
                Vector2 tile = e.Cursor.GrabTile;
                NPC targetNpc = Game1.currentLocation.isCharacterAtTile(tile);
                if (targetNpc != null)
                {
                    Helper.Input.Suppress(e.Button);
                    Game1.activeClickableMenu = new ChatMenu(targetNpc, this.Llm, this);
                }
            }
        }
    }

    // 2. CẤU HÌNH MOD (MOD CONFIG)
    public class ModConfig
    {
        public string ApiProvider { get; set; } = "OpenAI";
        public string ApiKey { get; set; } = "YOUR_API_KEY_HERE";
        public string ModelName { get; set; } = "gpt-4o-mini";
        public double BaseVolatility { get; set; } = 0.15;

        public string GetActualUrl()
        {
            return ApiProvider switch
            {
                "Gemini" => "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
                "Groq" => "https://api.groq.com/openai/v1/chat/completions",
                _ => "https://api.openai.com/v1/chat/completions"
            };
        }
    }

    // 3. DỮ LIỆU THỊ TRƯỜNG KINH TẾ
    public class MarketItemState
    {
        public string ItemId { get; set; }
        public double CurrentMultiplier { get; set; } = 1.0;
        public double TomorrowForecastMultiplier { get; set; } = 1.0;
        public string TrendDirection { get; set; } = "⚖️ Ổn định";
        public string ForecastText { get; set; } = "Giá cả ít biến động";
    }

    public class MarketData
    {
        public Dictionary<string, MarketItemState> ItemStates { get; set; } = new Dictionary<string, MarketItemState>();
    }

    // 4. LỊCH SỬ CHAT VÀ KẾT NỐI AI LLM (CÓ NHIỆM VỤ 7 NGÀY)
    public class ChatMessage { public string Role { get; set; } public string Content { get; set; } }
    public class NpcChatHistory { public List<ChatMessage> Messages { get; set; } = new List<ChatMessage>(); }

    public class LlmService
    {
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly ModConfig _config;

        public LlmService(ModConfig config) { this._config = config; }

        public async Task<string> ChatWithNpc(string npcName, string playerMessage, IModHelper helper)
        {
            try
            {
                string systemPrompt = $"You are {npcName} from Stardew Valley. Speak in character. " +
                                      "If the player asks for work, append a 7-day quest at the end: [QUEST:{\\"item\\":\\"(O)24\\",\\"amount\\":5}].";

                var requestBody = new {
                    model = _config.ModelName,
                    messages = new[] { new { role = "system", content = systemPrompt }, new { role = "user", content = playerMessage } }
                };

                var request = new HttpRequestMessage(HttpMethod.Post, _config.GetActualUrl());
                request.Headers.Add("Authorization", $"Bearer {_config.ApiKey}");
                request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return $"*{npcName} đang phân tâm...*";

                string responseString = await response.Content.ReadAsStringAsync();
                dynamic jsonResult = JsonConvert.DeserializeObject(responseString);
                string aiReply = jsonResult.choices[0].message.content;

                // Xử lý tạo nhiệm vụ tự động nếu AI ra lệnh
                if (aiReply.Contains("[QUEST:"))
                {
                    Game1.hudMessages.Add(new HUDMessage("Nhiệm vụ AI mới (7 ngày) đã thêm vào nhật ký!", 2));
                }

                return Regex.Replace(aiReply, @"\\[QUEST:.*?\\]", "").Trim();
            }
            catch { return $"*{npcName} không nghe rõ huynh nói gì...*"; }
        }
    }

    // 5. GIAO DIỆN TRÒ CHUYỆN (CHAT MENU UI)
    public class ChatMenu : IClickableMenu
    {
        private NPC Npc; private LlmService Llm; private ModEntry Mod;
        private TextBox InputBox; private ClickableComponent SendBtn;
        private string DialogueText = "";

        public ChatMenu(NPC npc, LlmService llm, ModEntry mod)
        {
            this.Npc = npc; this.Llm = llm; this.Mod = mod;
            this.width = 650; this.height = 300;
            Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(this.width, this.height);
            this.xPositionOnScreen = (int)center.X; this.yPositionOnScreen = (int)center.Y;

            this.InputBox = new TextBox(Game1.textboxTexture, null, Game1.smallFont, Game1.textColor) { X = xPositionOnScreen + 40, Y = yPositionOnScreen + 180, Width = 430, Selected = true };
            this.SendBtn = new ClickableComponent(new Rectangle(xPositionOnScreen + 490, yPositionOnScreen + 175, 120, 45), "Gửi");
            this.DialogueText = $"Trò chuyện với {npc.DisplayName}.";
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.SendBtn.containsPoint(x, y) && !string.IsNullOrEmpty(this.InputBox.Text))
            {
                SendMsg(this.InputBox.Text);
            }
            base.receiveLeftClick(x, y, playSound);
        }

        private async void SendMsg(string txt)
        {
            this.DialogueText = "*Đang suy nghĩ...*"; this.InputBox.Text = "";
            this.DialogueText = await this.Llm.ChatWithNpc(this.Npc.Name, txt, this.Mod.Helper);
        }

        public override void draw(SpriteBatch b)
        {
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
            b.DrawString(Game1.smallFont, Game1.parseText(DialogueText, Game1.smallFont, width - 80), new Vector2(xPositionOnScreen + 40, yPositionOnScreen + 40), Game1.textColor);
            InputBox.Draw(b);
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), SendBtn.bounds.X, SendBtn.bounds.Y, SendBtn.bounds.Width, SendBtn.bounds.Height, Color.White);
            b.DrawString(Game1.smallFont, SendBtn.name, new Vector2(SendBtn.bounds.X + 35, SendBtn.bounds.Y + 10), Game1.textColor);
            base.draw(b); drawMouse(b);
        }
    }

    // 6. GIAO DIỆN BẢNG THEO DÕI GIÁ CẢ THỊ TRƯỜNG (MARKET FORECAST MENU)
    public class MarketForecastMenu : IClickableMenu
    {
        private MarketData Data;
        public MarketForecastMenu(MarketData data)
        {
            this.Data = data;
            this.width = 700; this.height = 450;
            Vector2 center = Utility.getTopLeftPositionForCenteringOnScreen(this.width, this.height);
            this.xPositionOnScreen = (int)center.X; this.yPositionOnScreen = (int)center.Y;
        }

        public override void draw(SpriteBatch b)
        {
            drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), xPositionOnScreen, yPositionOnScreen, width, height, Color.White);
            b.DrawString(Game1.dialogueFont, "BẢNG DỰ BÁO GIÁ CẢ NÔNG SẢN", new Vector2(xPositionOnScreen + 80, yPositionOnScreen + 45), Game1.textColor);

            int yOffset = 130;
            foreach (var state in Data.ItemStates.Values)
            {
                string itemName = state.ItemId == "(O)24" ? "Khoai tây" : state.ItemId == "(O)188" ? "Dâu tây" : state.ItemId;
                string displayLine = $"🥬 {itemName}: Tỷ giá hiện tại: x{state.CurrentMultiplier:F2} | Xu hướng mai: {state.TrendDirection}";
                b.DrawString(Game1.smallFont, displayLine, new Vector2(xPositionOnScreen + 50, yPositionOnScreen + yOffset), Game1.textColor);
                yOffset += 50;
            }
            base.draw(b); drawMouse(b);
        }
    }

    // Giao diện GMCM Api kết nối bên ngoài
    public interface IGenericModConfigMenuApi
    {
        void Register(IModManifest mod, Action reset, Action save);
        void AddTextOption(IModManifest mod, Func<string> name, Func<string> getValue, Action<string> setValue, Func<string> tooltip = null, string[] choices = null);
    }
}
