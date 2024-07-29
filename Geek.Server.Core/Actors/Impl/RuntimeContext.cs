using System.Runtime.CompilerServices;

namespace Geek.Server.Core.Actors.Impl
{
	internal class RuntimeContext
	{
		// 获取当前调用链 ID 的属性
		internal static long CurChainId => chainCtx.Value;

		// 获取当前 actor ID 的属性
		internal static long CurActor => actorCtx.Value;

		// 使用 AsyncLocal<long> 存储调用链 ID
		internal static AsyncLocal<long> chainCtx = new();

		// 使用 AsyncLocal<long> 存储 actor ID
		internal static AsyncLocal<long> actorCtx = new();

		// 设置上下文，存储调用链 ID 和 actor ID
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void SetContext(long callChainId, long actorId)
		{
			chainCtx.Value = callChainId;
			actorCtx.Value = actorId;
		}

		// 重置上下文，将调用链 ID 和 actor ID 置为 0
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		internal static void ResetContext()
		{
			chainCtx.Value = 0;
			actorCtx.Value = 0;
		}
	}
}