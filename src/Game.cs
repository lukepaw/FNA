#region License
/* FNA - XNA4 Reimplementation for Desktop Platforms
 * Copyright 2009-2021 Ethan Lee and the MonoGame Team
 *
 * Released under the Microsoft Public License.
 * See LICENSE for details.
 */
#endregion

#region Using Statements
using System;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input.Touch;
#endregion

namespace Microsoft.Xna.Framework
{
	public class Game : IDisposable
	{
		#region Public Properties

		public GraphicsDevice GraphicsDevice
		{
			get
			{
				if (graphicsDeviceService == null)
				{
					graphicsDeviceService = (IGraphicsDeviceService)
						Services.GetService(typeof(IGraphicsDeviceService));

					if (graphicsDeviceService == null)
					{
						throw new InvalidOperationException(
							"No Graphics Device Service"
						);
					}
				}
				return graphicsDeviceService.GraphicsDevice;
			}
		}

		private TimeSpan INTERNAL_inactiveSleepTime;
		public TimeSpan InactiveSleepTime
		{
			get
			{
				return INTERNAL_inactiveSleepTime;
			}
			set
			{
				if (value < TimeSpan.Zero)
				{
					throw new ArgumentOutOfRangeException(
						"The time must be positive.",
						default(Exception)
					);
				}
				if (INTERNAL_inactiveSleepTime != value)
				{
					INTERNAL_inactiveSleepTime = value;
				}
			}
		}

		private bool INTERNAL_isActive;
		public bool IsActive
		{
			get
			{
				return INTERNAL_isActive;
			}
			internal set
			{
				if (INTERNAL_isActive != value)
				{
					INTERNAL_isActive = value;
					if (INTERNAL_isActive)
					{
						OnActivated(this, EventArgs.Empty);
					}
					else
					{
						OnDeactivated(this, EventArgs.Empty);
					}
				}
			}
		}

		// LUKE: Removed IsFixedTimeStep

		private bool INTERNAL_isMouseVisible;
		public bool IsMouseVisible
		{
			get
			{
				return INTERNAL_isMouseVisible;
			}
			set
			{
				if (INTERNAL_isMouseVisible != value)
				{
					INTERNAL_isMouseVisible = value;
					FNAPlatform.OnIsMouseVisibleChanged(value);
				}
			}
		}

		public LaunchParameters LaunchParameters
		{
			get;
			private set;
		}

		private TimeSpan INTERNAL_targetElapsedTime;
		public TimeSpan TargetElapsedTime
		{
			get
			{
				return INTERNAL_targetElapsedTime;
			}
			set
			{
				if (value <= TimeSpan.Zero)
				{
					throw new ArgumentOutOfRangeException(
						"The time must be positive and non-zero.",
						default(Exception)
					);
				}

				INTERNAL_targetElapsedTime = value;
			}
		}

		public GameServiceContainer Services
		{
			get;
			private set;
		}

		public GameWindow Window
		{
			get;
			private set;
		}

		#endregion

		#region Internal Variables

		internal bool RunApplication;

		#endregion

		#region Private Variables

		private IGraphicsDeviceService graphicsDeviceService;
		private IGraphicsDeviceManager graphicsDeviceManager;
		private GraphicsAdapter currentAdapter;
		private bool hasInitialized;
		private bool suppressDraw;
		private bool isDisposed;

		private readonly GameTime gameTime;
		private Stopwatch gameTimer;
		private long previousTicks = 0;

		// LUKE: Removed MaxElapsedTime

		private bool[] textInputControlDown;
		private int[] textInputControlRepeat;
		private bool textInputSuppress;

		#endregion

		#region Events

		public event EventHandler<EventArgs> Activated;
		public event EventHandler<EventArgs> Deactivated;
		public event EventHandler<EventArgs> Disposed;
		public event EventHandler<EventArgs> Exiting;

		#endregion

		#region Public Constructor

		public Game()
		{
			AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

			LaunchParameters = new LaunchParameters();
			Services = new GameServiceContainer();
		
			IsMouseVisible = false;
			TargetElapsedTime = TimeSpan.FromTicks(166667); // 60fps
			InactiveSleepTime = TimeSpan.FromSeconds(0.02);

			textInputControlDown = new bool[FNAPlatform.TextInputCharacters.Length];
			textInputControlRepeat = new int[FNAPlatform.TextInputCharacters.Length];

			hasInitialized = false;
			suppressDraw = false;
			isDisposed = false;

			gameTime = new GameTime();

			Window = FNAPlatform.CreateWindow();
			Mouse.WindowHandle = Window.Handle;
			TouchPanel.WindowHandle = Window.Handle;

			FrameworkDispatcher.Update();

			// Ready to run the loop!
			RunApplication = true;
		}

		#endregion

		#region Destructor

		~Game()
		{
			Dispose(false);
		}

		#endregion

		#region IDisposable Implementation

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
			if (Disposed != null)
			{
				Disposed(this, EventArgs.Empty);
			}
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!isDisposed)
			{
				if (disposing)
				{
					if (graphicsDeviceService != null)
					{
						// FIXME: Does XNA4 require the GDM to be disposable? -flibit
						(graphicsDeviceService as IDisposable).Dispose();
					}

					if (Window != null)
					{
						FNAPlatform.DisposeWindow(Window);
					}
				}

				AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;

				isDisposed = true;
			}
		}

		[DebuggerNonUserCode]
		private void AssertNotDisposed()
		{
			if (isDisposed)
			{
				string name = GetType().Name;
				throw new ObjectDisposedException(
					name,
					string.Format(
						"The {0} object was used after being Disposed.",
						name
					)
				);
			}
		}

		#endregion

		#region Public Methods

		public void Exit()
		{
			RunApplication = false;
			suppressDraw = true;
		}

		public void SuppressDraw()
		{
			suppressDraw = true;
		}

		public void RunOneFrame()
		{
			if (!hasInitialized)
			{
				DoInitialize();
				gameTimer = Stopwatch.StartNew();
				hasInitialized = true;
			}

			FNAPlatform.PollEvents(
				this,
				ref currentAdapter,
				textInputControlDown,
				textInputControlRepeat,
				ref textInputSuppress
			);
			Tick();
		}

		public void Run()
		{
			AssertNotDisposed();

			if (!hasInitialized)
			{
				DoInitialize();
				hasInitialized = true;
			}

			BeginRun();
			BeforeLoop();

			gameTimer = Stopwatch.StartNew();
			RunLoop();

			EndRun();
			AfterLoop();
		}

		public void Tick()
		{
			/* NOTE: This code is very sensitive and can break very badly,
			 * even with what looks like a safe change. Be sure to test
			 * any change fully in both the fixed and variable timestep
			 * modes across multiple devices and platforms.
			 */

			// Advance the accumulated elapsed time.
			long currentTicks = gameTimer.Elapsed.Ticks;
			TimeSpan elapsedTime = TimeSpan.FromTicks(currentTicks - previousTicks);
			previousTicks = currentTicks;

			// LUKE: Removed MaxElapsedTime

			gameTime.ElapsedGameTime = elapsedTime;
			gameTime.TotalGameTime += elapsedTime;

			AssertNotDisposed();
			Update(gameTime);

			// Draw unless the update suppressed it.
			if (suppressDraw)
			{
				suppressDraw = false;
			}
			else
			{
				/* Draw/EndDraw should not be called if BeginDraw returns false.
				 * http://stackoverflow.com/questions/4054936/manual-control-over-when-to-redraw-the-screen/4057180#4057180
				 * http://stackoverflow.com/questions/4235439/xna-3-1-to-4-0-requires-constant-redraw-or-will-display-a-purple-screen
				 */
				if (BeginDraw())
				{
					Draw(gameTime);
					EndDraw();
				}
			}
		}

		#endregion

		#region Internal Methods

		internal void RedrawWindow()
		{
			/* Draw/EndDraw should not be called if BeginDraw returns false.
			 * http://stackoverflow.com/questions/4054936/manual-control-over-when-to-redraw-the-screen/4057180#4057180
			 * http://stackoverflow.com/questions/4235439/xna-3-1-to-4-0-requires-constant-redraw-or-will-display-a-purple-screen
			 *
			 * Additionally, if we haven't even started yet, be quiet until we have!
			 * -flibit
			 */
			if (gameTime.TotalGameTime != TimeSpan.Zero && BeginDraw())
			{
				Draw(new GameTime(gameTime.TotalGameTime, TimeSpan.Zero));
				EndDraw();
			}
		}

		#endregion

		#region Protected Methods

		protected virtual bool BeginDraw()
		{
			if (graphicsDeviceManager != null)
			{
				return graphicsDeviceManager.BeginDraw();
			}
			return true;
		}

		protected virtual void EndDraw()
		{
			if (graphicsDeviceManager != null)
			{
				graphicsDeviceManager.EndDraw();
			}
		}

		protected virtual void BeginRun()
		{
		}

		protected virtual void EndRun()
		{
		}

		protected virtual void Initialize()
		{
		}

		protected virtual void Draw(GameTime gameTime)
		{
		}

		protected virtual void Update(GameTime gameTime)
		{
			FrameworkDispatcher.Update();
		}

		protected virtual void OnExiting(object sender, EventArgs args)
		{
			if (Exiting != null)
			{
				Exiting(this, args);
			}
		}

		protected virtual void OnActivated(object sender, EventArgs args)
		{
			AssertNotDisposed();
			if (Activated != null)
			{
				Activated(this, args);
			}
		}

		protected virtual void OnDeactivated(object sender, EventArgs args)
		{
			AssertNotDisposed();
			if (Deactivated != null)
			{
				Deactivated(this, args);
			}
		}

		protected virtual bool ShowMissingRequirementMessage(Exception exception)
		{
			if (exception is NoAudioHardwareException)
			{
				FNAPlatform.ShowRuntimeError(
					Window.Title,
					"Could not find a suitable audio device. " +
					" Verify that a sound card is\ninstalled," +
					" and check the driver properties to make" +
					" sure it is not disabled."
				);
				return true;
			}
			if (exception is NoSuitableGraphicsDeviceException)
			{
				FNAPlatform.ShowRuntimeError(
					Window.Title,
					"Could not find a suitable graphics device." +
					" More information:\n\n" + exception.Message
				);
				return true;
			}
			return false;
		}

		#endregion

		#region Private Methods

		private void DoInitialize()
		{
			AssertNotDisposed();

			/* If this is late, you can still create it yourself.
			 * In fact, you can even go as far as creating the
			 * _manager_ before base.Initialize(), but Begin/EndDraw
			 * will not get called. Just... please, make the service
			 * before calling Run().
			 */
			graphicsDeviceManager = (IGraphicsDeviceManager)
				Services.GetService(typeof(IGraphicsDeviceManager));
			if (graphicsDeviceManager != null)
			{
				graphicsDeviceManager.CreateDevice();
			}

			Initialize();
		}

		private void BeforeLoop()
		{
			currentAdapter = FNAPlatform.RegisterGame(this);
			IsActive = true;

			// Perform initial check for a touch device
			TouchPanel.TouchDeviceExists = FNAPlatform.GetTouchCapabilities().IsConnected;
		}

		private void AfterLoop()
		{
			FNAPlatform.UnregisterGame(this);
		}

		private void RunLoop()
		{
			/* Some platforms (i.e. Emscripten) don't support
			 * indefinite while loops, so instead we have to
			 * surrender control to the platform's main loop.
			 * -caleb
			 */
			if (FNAPlatform.NeedsPlatformMainLoop())
			{
				/* This breaks control flow and jumps
				 * directly into the platform main loop.
				 * Nothing below this call will be executed.
				 */
				FNAPlatform.RunPlatformMainLoop(this);
			}

			while (RunApplication)
			{
				FNAPlatform.PollEvents(
					this,
					ref currentAdapter,
					textInputControlDown,
					textInputControlRepeat,
					ref textInputSuppress
				);
				Tick();
			}
			OnExiting(this, EventArgs.Empty);
		}

		#endregion

		#region Private Event Handlers

		private void OnUnhandledException(
			object sender,
			UnhandledExceptionEventArgs args
		) {
			ShowMissingRequirementMessage(args.ExceptionObject as Exception);
		}

		#endregion
	}
}
