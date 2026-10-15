using System;
using System.Text.Json;
using Pineglow.Launcher.Models;
using Xunit;
namespace Pineglow.Launcher.Tests;
public sealed class GamePlaySessionTests
{
 [Fact]
 public void RecordPlaySession_PreservesLegacyTotalsAndRecordsActualInterval()
 {
  var i=new GameInstance { TotalPlaySeconds=7200, PlayCount=9 };
  var start=new DateTimeOffset(2026,10,14,23,45,0,TimeSpan.FromHours(8));
  i.RecordPlaySession(start,start.AddMinutes(30));
  Assert.Equal(9000,i.TotalPlaySeconds);Assert.Equal(9,i.PlayCount);
  var session=Assert.Single(i.PlaySessions);Assert.Equal(start,session.StartedAt);Assert.Equal(start.AddMinutes(30),session.EndedAt);
  var saved=JsonSerializer.Deserialize<GameInstance>(JsonSerializer.Serialize(i))!;
  Assert.Equal(9000,saved.TotalPlaySeconds);Assert.Equal(session.StartedAt,Assert.Single(saved.PlaySessions).StartedAt);
 }
 [Fact]
 public void RecordPlaySession_InvalidIntervalsDoNotCreateRecordsOrChangeTotals()
 {
  var i=new GameInstance { TotalPlaySeconds=30 };var start=DateTimeOffset.UtcNow;
  i.RecordPlaySession(start,start);i.RecordPlaySession(start,start.AddSeconds(-1));i.RecordPlaySession(start,start.AddMilliseconds(100));
  Assert.Empty(i.PlaySessions);Assert.Equal(30,i.TotalPlaySeconds);
 }
 [Fact]
 public void LegacyConfig_DoesNotFabricateDatedSessions_AndNullListCanRecord()
 {
  var i=JsonSerializer.Deserialize<GameInstance>("{\"TotalPlaySeconds\":5400,\"PlayCount\":5}")!;
  Assert.Empty(i.PlaySessions);Assert.Equal(5400,i.TotalPlaySeconds);
  i.PlaySessions=null!;var start=DateTimeOffset.UtcNow;i.RecordPlaySession(start,start.AddSeconds(15));
  Assert.Single(i.PlaySessions);Assert.Equal(5415,i.TotalPlaySeconds);
 }
}
