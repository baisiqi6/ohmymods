using System;
using KingdomEnhancedMod;
using UnityEngine;
int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
var scene=new Transform();var kingdom=new Kingdom{playerOne=new(){wallet=new()},playerTwo=new(){wallet=new()}};
void Read(string expected){PatchUI_CalendarGems.Refresh(kingdom,scene);Check(PatchUI_CalendarGems.ValueText==expected,"expected "+expected+" got "+PatchUI_CalendarGems.ValueText);Check(PatchUI_CalendarGems.CaptionText=="储存钻石","caption stable");}
var a=new CampaignSaveData{Stored=96};var b=new CampaignSaveData{Pointer=(IntPtr)3,Stored=7};
var g=GlobalSaveData.Raw=new(){campaigns=new(){a,b}};
Read("96");a.Stored=93;Read("93");a.Stored=97;Read("97");a.Stored=0;Read("0");
g._currentCampaign=1;Read("7");g._currentCampaign=0;a.Stored=1234;Read("1,234");
g._currentCampaign=-1;Read("—");g._currentCampaign=2;Read("—");g._currentCampaign=0;
g._currentChallenge=10;Read("—");g._currentChallenge=-1;Read("—");g._currentChallenge=0;
int before=GlobalSaveData.RawReads;NetworkBigBoss.Online=true;Read("—");Check(GlobalSaveData.RawReads==before,"online never reads local save");NetworkBigBoss.Online=false;
a.Stored=-1;Read("—");a.Stored=9;a.ThrowOnStored=true;Read("—");a.ThrowOnStored=false;Read("9");
a.Pointer=IntPtr.Zero;Read("—");a.Pointer=(IntPtr)2;g.campaigns[0]=null;Read("—");g.campaigns[0]=a;
g.campaigns=null;Read("—");g.campaigns=new(){a,b};g.Pointer=IntPtr.Zero;Read("—");g.Pointer=(IntPtr)1;
GlobalSaveData.ThrowOnRaw=true;Read("—");GlobalSaveData.ThrowOnRaw=false;
NetworkBigBoss.ThrowOnRead=true;Read("—");NetworkBigBoss.ThrowOnRead=false;
GlobalSaveData.Raw=null;Read("—");GlobalSaveData.Raw=new(){campaigns=new(){new(){Stored=5}}};Read("5");
GlobalSaveData.Raw=g;Read("9");before=GlobalSaveData.RawReads;PatchUI_CalendarGems.Refresh(kingdom,null);Check(PatchUI_CalendarGems.ValueText=="—"&&GlobalSaveData.RawReads==before,"null scene clears without raw read");
Read("9");PatchUI_CalendarGems.Clear();Check(PatchUI_CalendarGems.ValueText=="—"&&PatchUI_CalendarGems.CaptionText=="储存钻石","Clear never keeps stale balance");
Check(Wallet.Reads==0&&Wallet.Writes==0,"two wallets never read or summed");Check(CampaignSaveData.Writes==0,"storedGems never written");
Check(GlobalSaveData.LoadedReads==0&&GlobalSaveData.SelectorReads==0,"no constructing getter or implicit selector");
Check(BepInEx.Logging.ManualLogSource.Warnings.Count==1,"read faults log once");
Console.WriteLine($"ALL PASS ({checks} storage assertions)");
