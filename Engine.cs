using System.Reflection;
using System.Text.Json;
using Silk.NET.Maths;
using TheAdventure.Models;
using TheAdventure.Models.Data;
using TheAdventure.Scripting;

namespace TheAdventure;

public class Engine
{
    private readonly GameRenderer _renderer;
    private readonly Input _input;
    private readonly ScriptEngine _scriptEngine = new();

    private readonly Dictionary<int, GameObject> _gameObjects = new();
    private readonly Dictionary<string, TileSet> _loadedTileSets = new();
    private readonly Dictionary<int, Tile> _tileIdMap = new();

    private Level _currentLevel = new();
    private PlayerObject? _player;

    private DateTimeOffset _lastUpdate = DateTimeOffset.Now;
    private List<HealthPickup> _healthPickups = new();

    public Engine(GameRenderer renderer, Input input)
    {
        _renderer = renderer;
        _input = input;

        _input.OnMouseClick += (_, coords) => AddBomb(coords.x, coords.y);
    }

    public void SetupWorld()
    {
        _player = new(SpriteSheet.Load(_renderer, "Player.json", "Assets"), 100, 100);

        var levelContent = File.ReadAllText(Path.Combine("Assets", "terrain.tmj"));
        var level = JsonSerializer.Deserialize<Level>(levelContent);
        var healthSprite = SpriteSheet.Load(_renderer, "HealthPickup.json", "Assets");
        // Place health pickups far from player to avoid instant collection
        _healthPickups.Add(new HealthPickup(healthSprite, (300, 200)));
        _healthPickups.Add(new HealthPickup(healthSprite, (500, 300)));

        if (level == null)
            throw new Exception("Failed to load level");

        foreach (var tileSetRef in level.TileSets)
        {
            var tileSetContent = File.ReadAllText(Path.Combine("Assets", tileSetRef.Source));
            var tileSet = JsonSerializer.Deserialize<TileSet>(tileSetContent);
            if (tileSet == null)
                throw new Exception("Failed to load tile set");

            foreach (var tile in tileSet.Tiles)
            {
                tile.TextureId = _renderer.LoadTexture(Path.Combine("Assets", tile.Image), out _);
                _tileIdMap.Add(tile.Id!.Value, tile);
            }

            _loadedTileSets.Add(tileSet.Name, tileSet);
        }

        if (level.Width == null || level.Height == null)
            throw new Exception("Invalid level dimensions");

        if (level.TileWidth == null || level.TileHeight == null)
            throw new Exception("Invalid tile dimensions");

        _renderer.SetWorldBounds(new Rectangle<int>(0, 0, level.Width.Value * level.TileWidth.Value,
            level.Height.Value * level.TileHeight.Value));

        _currentLevel = level;

        _scriptEngine.LoadAll(Path.Combine("Assets", "Scripts"));
    }

    public void ProcessFrame()
    {
        var currentTime = DateTimeOffset.Now;
        var msSinceLastFrame = (currentTime - _lastUpdate).TotalMilliseconds;
        _lastUpdate = currentTime;

        if (_player == null)
            return;

        double up = _input.IsUpPressed() ? 1.0 : 0.0;
        double down = _input.IsDownPressed() ? 1.0 : 0.0;
        double left = _input.IsLeftPressed() ? 1.0 : 0.0;
        double right = _input.IsRightPressed() ? 1.0 : 0.0;
        bool isAttacking = _input.IsKeyAPressed() && (up + down + left + right <= 1);
        bool addBomb = _input.IsKeyBPressed();

        _player.UpdatePosition(up, down, left, right, 48, 48, msSinceLastFrame);
        if (isAttacking) _player.Attack();

        _scriptEngine.ExecuteAll(this);

        if (addBomb)
            AddBomb(_player.Position.X, _player.Position.Y, false);
    }

    public void RenderFrame()
    {
        _renderer.SetDrawColor(0, 0, 0, 255);
        _renderer.ClearScreen();

        var playerPosition = _player!.Position;
        _renderer.CameraLookAt(playerPosition.X, playerPosition.Y);

        RenderTerrain();
        RenderAllObjects();

        _renderer.PresentFrame();
    }

    public void RenderAllObjects()
    {
        var toRemove = new List<int>();
        foreach (var gameObject in GetRenderables())
        {
            gameObject.Render(_renderer);
            if (gameObject is TemporaryGameObject { IsExpired: true } tempGameObject)
            {
                toRemove.Add(tempGameObject.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _gameObjects.Remove(id, out var gameObject);

            if (_player == null) continue;

            var tempGameObject = (TemporaryGameObject)gameObject!;
            var deltaX = Math.Abs(_player.Position.X - tempGameObject.Position.X);
            var deltaY = Math.Abs(_player.Position.Y - tempGameObject.Position.Y);
            if (deltaX < 32 && deltaY < 32)
            {
                _player.TakeDamage(25); // Take 25 damage from bomb
            }
        }

        foreach (var pickup in _healthPickups)
        {
            pickup.Render(_renderer);
        }

        if (_player != null)
        {
            for (int i = _healthPickups.Count - 1; i >= 0; i--)
            {
                var pickup = _healthPickups[i];
                var dx = Math.Abs(_player.Position.X - pickup.Position.X);
                var dy = Math.Abs(_player.Position.Y - pickup.Position.Y);
                if (dx < 32 && dy < 32 && _player.CurrentHealth < _player.MaxHealth)
                {
                    _player.Heal(pickup.HealAmount);
                    _healthPickups.RemoveAt(i);
                }
            }

            _player.Render(_renderer);

            // Draw health bar at fixed screen position (top-left corner)
            var barWidth = 120;
            var barHeight = 16;
            var healthPercent = (float)_player.CurrentHealth / _player.MaxHealth;
            var barX = 16; // screen X
            var barY = 16; // screen Y

            _renderer.SetDrawColor(200, 40, 40, 255); // Red background
            _renderer.FillRect(barX, barY, barWidth, barHeight);
            _renderer.SetDrawColor(40, 200, 40, 255); // Green foreground
            _renderer.FillRect(barX, barY, (int)(barWidth * healthPercent), barHeight);
        }
    }

    public void RenderTerrain()
    {
        foreach (var layer in _currentLevel.Layers)
        {
            for (int i = 0; i < _currentLevel.Width; ++i)
            {
                for (int j = 0; j < _currentLevel.Height; ++j)
                {
                    int? dataIndex = j * layer.Width + i;
                    if (dataIndex == null) continue;

                    var currentTileId = layer.Data[dataIndex.Value] - 1;
                    if (currentTileId == null) continue;

                    var tile = _tileIdMap[currentTileId.Value];
                    var tw = tile.ImageWidth ?? 0;
                    var th = tile.ImageHeight ?? 0;

                    _renderer.RenderTexture(tile.TextureId,
                        new Rectangle<int>(0, 0, tw, th),
                        new Rectangle<int>(i * tw, j * th, tw, th));
                }
            }
        }
    }

    public IEnumerable<RenderableGameObject> GetRenderables()
    {
        foreach (var gameObject in _gameObjects.Values)
        {
            if (gameObject is RenderableGameObject renderable)
                yield return renderable;
        }
    }

    public (int X, int Y) GetPlayerPosition() => _player!.Position;

    public void AddBomb(int X, int Y, bool translateCoordinates = true)
    {
        var worldCoords = translateCoordinates ? _renderer.ToWorldCoordinates(X, Y) : new Vector2D<int>(X, Y);

        var spriteSheet = SpriteSheet.Load(_renderer, "BombExploding.json", "Assets");
        spriteSheet.ActivateAnimation("Explode");

        var bomb = new TemporaryGameObject(spriteSheet, 2.1, (worldCoords.X, worldCoords.Y));
        _gameObjects.Add(bomb.Id, bomb);
    }
}
