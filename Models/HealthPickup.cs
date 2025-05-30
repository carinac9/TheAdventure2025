using TheAdventure.Models;
using Silk.NET.Maths;
using Silk.NET.SDL;

namespace TheAdventure.Models
{
    public class HealthPickup : RenderableGameObject
    {
        public int HealAmount { get; set; } = 25;

        public HealthPickup(SpriteSheet spriteSheet, (int X, int Y) position, int healAmount = 25)
            : base(spriteSheet, position)
        {
            HealAmount = healAmount;
            spriteSheet.ActivateAnimation("Idle");
        }

        public override void Render(GameRenderer renderer)
        {
            // Draw the heart at 24x24 pixels, centered on the pickup position
            var src = new Rectangle<int>(0, 0, SpriteSheet.FrameWidth, SpriteSheet.FrameHeight);
            var dst = new Rectangle<int>(Position.X - 12, Position.Y - 12, 24, 24);
            renderer.RenderTexture(SpriteSheet.GetTextureId(), src, dst);
        }
    }
}
