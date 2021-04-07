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
#endregion

namespace Microsoft.Xna.Framework
{
	public class GameTime
	{
		#region Public Properties

		public TimeSpan TotalGameTime
		{
			get;
			set;
		}

		public TimeSpan ElapsedGameTime
		{
			get;
			set;
		}

		#endregion

		#region Public Constructors

		public GameTime()
		{
			TotalGameTime = TimeSpan.Zero;
			ElapsedGameTime = TimeSpan.Zero;
		}

		public GameTime(TimeSpan totalGameTime, TimeSpan elapsedGameTime)
		{
			TotalGameTime = totalGameTime;
			ElapsedGameTime = elapsedGameTime;
		}

		#endregion
	}
}
