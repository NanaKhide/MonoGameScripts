/* 
 This is a Fix for a MonoGame / XNA bug that causes the game window not to draw when resizing.
This is suboptimal, but it works around the issue by manually handling the window's resize events
and applying the changes to the graphics device in real-time, allowing the game to continue rendering.
This code is designed to be used in a MonoGame DirectX project and requires the MonoGame.Framework and System.Windows.Forms namespaces.

Performance will be affected during resizing, but the game will remain responsive and render correctly.

 */


using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Keys = Microsoft.Xna.Framework.Input.Keys;

namespace SystemDefect
{
    public class Game1 : Game
    {
        // MonoGame
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;
        private Texture2D _texture;
        private KeyboardState _previousKeyboardState;

        // Virtual Resolution & Scaling
        private readonly int _virtualWindowWidth = 800;
        private readonly int _virtualWindowHeight = 600;
        private RenderTarget2D _resizeRenderTarget;
        private Rectangle _resizeRenderSize;

        // Borderless Fullscreen State
        private bool _isBorderless = false;
        private System.Drawing.Point _windowedPosition;
        private System.Drawing.Size _windowedSize;

        // Win32 Interop & Resizing
        private Form _form;
        private Timer _resizeTimer;
        private IntPtr _originalWndProc;
        private WndProc _wndProcDelegate;

        #region Win32
        // This region contains all the low-level Windows API code needed to hook into

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int GWLP_WNDPROC = -4;
        private const uint WM_ENTERSIZEMOVE = 0x0231; // Fired when the user first clicks the title bar or a resize handle.
        private const uint WM_EXITSIZEMOVE = 0x0232;  // Fired when the user releases the mouse button.
        #endregion

        public Game1()
        {
            _graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";
            IsMouseVisible = true;
            Window.AllowUserResizing = true;
        }

        protected override void Initialize()
        {
            base.Initialize();

            _graphics.PreferredBackBufferWidth = _virtualWindowWidth;
            _graphics.PreferredBackBufferHeight = _virtualWindowHeight;
            _graphics.ApplyChanges();

            _form = (Form)Control.FromHandle(Window.Handle);

            // Store the initial window state for the borderless toggle. This prevents a bug
            // where the window would return to a zero size if F11 is pressed before any resize.
            _windowedPosition = _form.Location;
            _windowedSize = _form.Size;

            // Hook into the window's message loop to intercept low-level messages.
            _wndProcDelegate = new WndProc(MyWndProc);
            _originalWndProc = SetWindowLongPtr(_form.Handle, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_wndProcDelegate));

            CalculateResizeRenderSize();
        }

        /// <summary>
        /// A custom window procedure to intercept specific Windows messages.
        /// </summary>
        private IntPtr MyWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                // WM_ENTERSIZEMOVE is sent when the user starts resizing or moving the window.
                case WM_ENTERSIZEMOVE:
                    if (_resizeTimer == null)
                    {
                        // Use a timer to tick our game's update/draw loop manually
                        // because the default MonoGame loop is blocked during a resize/move.
                        _resizeTimer = new Timer { Interval = 16 }; // ~60fps
                        _resizeTimer.Tick += OnResizeTick;
                    }
                    _resizeTimer.Start();
                    break;

                // WM_EXITSIZEMOVE reliably tells us when the operation is finished.
                case WM_EXITSIZEMOVE:
                    _resizeTimer?.Stop();

                    // Apply one final, clean resize to ensure the back buffer matches the final window size.
                    System.Drawing.Point finalLocation = _form.Location;
                    _graphics.PreferredBackBufferWidth = _form.ClientSize.Width;
                    _graphics.PreferredBackBufferHeight = _form.ClientSize.Height;
                    _graphics.ApplyChanges();
                    _form.Location = finalLocation; // Restore location to prevent jumping.
                    break;
            }
            // Pass all other messages on to the original handler to ensure normal window behavior.
            return CallWindowProc(_originalWndProc, hWnd, msg, wParam, lParam);
        }

        /// <summary>
        /// This method is called by the timer during a resize operation.
        /// </summary>
        private void OnResizeTick(object sender, EventArgs e)
        {
            // Only apply changes if the window size has actually changed.
            if (_graphics.PreferredBackBufferWidth != _form.ClientSize.Width ||
                _graphics.PreferredBackBufferHeight != _form.ClientSize.Height)
            {
                // The ApplyChanges() method can sometimes move the window.
                // Save and restore the location to prevent any jumping or jitter.
                System.Drawing.Point location = _form.Location;
                _graphics.PreferredBackBufferWidth = _form.ClientSize.Width;
                _graphics.PreferredBackBufferHeight = _form.ClientSize.Height;
                _graphics.ApplyChanges();
                _form.Location = location;
            }
            // Manually tick the game's logic and drawing to keep it responsive.
            this.Tick();
        }

        /// <summary>
        /// Toggles between windowed and borderless fullscreen mode.
        /// </summary>
        private void SetBorderless(bool enabled)
        {
            if (enabled)
            {
                _windowedPosition = _form.Location;
                _windowedSize = _form.Size;

                // Detect which screen the window is on for correct multi-monitor support.
                Screen currentScreen = Screen.FromControl(_form);

                Window.IsBorderless = true;
                _graphics.PreferredBackBufferWidth = currentScreen.Bounds.Width;
                _graphics.PreferredBackBufferHeight = currentScreen.Bounds.Height;
                _graphics.ApplyChanges();

                // Move the window to the top-left corner of the correct screen.
                _form.Location = currentScreen.Bounds.Location;
            }
            else // Restore windowed mode
            {
                Window.IsBorderless = false;
                _graphics.PreferredBackBufferWidth = _windowedSize.Width;
                _graphics.PreferredBackBufferHeight = _windowedSize.Height;
                _graphics.ApplyChanges();
                _form.Location = _windowedPosition;
            }
        }

        /// <summary>
        /// Calculates the destination rectangle for the letterboxed/pillarboxed scene.
        /// </summary>
        private void CalculateResizeRenderSize()
        {
            Point size = new Point(_form.ClientSize.Width, _form.ClientSize.Height);
            float scaleX = (float)size.X / _virtualWindowWidth;
            float scaleY = (float)size.Y / _virtualWindowHeight;
            float scale = Math.Min(scaleX, scaleY); // Use the smaller scale to maintain aspect ratio
            _resizeRenderSize.Width = (int)(_virtualWindowWidth * scale);
            _resizeRenderSize.Height = (int)(_virtualWindowHeight * scale);

            // Center the rectangle within the window
            _resizeRenderSize.X = (size.X - _resizeRenderSize.Width) / 2;
            _resizeRenderSize.Y = (size.Y - _resizeRenderSize.Height) / 2;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            _resizeRenderTarget = new RenderTarget2D(GraphicsDevice, _virtualWindowWidth, _virtualWindowHeight);
            _texture = Content.Load<Texture2D>("Icon");
        }

        protected override void Update(GameTime gameTime)
        {
            KeyboardState currentKeyboardState = Keyboard.GetState();

            // Toggle borderless fullscreen with F11. This is added for testing purposes.
            if (currentKeyboardState.IsKeyDown(Keys.F11) && _previousKeyboardState.IsKeyUp(Keys.F11))
            {
                _isBorderless = !_isBorderless;
                SetBorderless(_isBorderless);
            }

            _previousKeyboardState = currentKeyboardState;
            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // STEP 1: Draw the entire game scene to an off-screen texture
            GraphicsDevice.SetRenderTarget(_resizeRenderTarget);
            GraphicsDevice.Clear(Color.CornflowerBlue);
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(_texture, new Vector2(400, 400), Color.White);
            _spriteBatch.End();
            base.Draw(gameTime);

            // STEP 2: Draw that texture to the actual back buffer, scaling it up or down
            // with letterboxing to fit the final window size.
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            CalculateResizeRenderSize();
            _spriteBatch.Draw(_resizeRenderTarget, _resizeRenderSize, Color.White);
            _spriteBatch.End();
        }
    }
}