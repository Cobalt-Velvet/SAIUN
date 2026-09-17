using System.Runtime.CompilerServices;

// 테스트 어셈블리가 internal 멤버(가짜 시계 주입 등)에 접근할 수 있게 한다.
[assembly: InternalsVisibleTo("_SAIUN.Tests")]
