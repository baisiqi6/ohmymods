from pathlib import Path
import re
root=Path(__file__).resolve().parents[4]
src=(root/'il2cpp/HeroShop.cs').read_text(encoding='utf8')
body=src[src.index('internal static class HeroShop\n'):].replace('\n#endif','')
for old,new in [('HeroShop','MusketeerShop'),('HeroShopOwner','MusketeerShopOwner'),('HeroShopPayment','MusketeerShopPayment')]:body=re.sub(r'\b'+old+r'\b',new,body)
body=body.replace('internal const int Price = 8;','internal const int Price = 4;')
body=body.replace('ModConfig.HeroArcherEnabled','ModConfig.MusketeerEnabled').replace('KEM_HeroShop','KEM_MusketeerShop').replace('KingdomEnhancedMod.HeroShop.png','KingdomEnhancedMod.MusketeerShop.png')
body=body.replace('HeroRecruitment.StatusText','MusketeerIdentity.StatusText').replace('!HeroRecruitment.CanPurchase','!MusketeerIdentity.CanPurchase || RackCount() >= 3')
body=body.replace('success = HeroRecruitment.TryPurchase(out reason)','success = TryCreateGun(out reason)')
body=body.replace('                    HeroShopBannerVisuals.Tick(_object, _renderer);\n','').replace('        HeroShopBannerVisuals.Tick(_object, _renderer);\n','').replace('                HeroShopBannerVisuals.Clear();\n','')
body=body.replace('已购名额保留','已购职业与枪具记录保留').replace('英雄商店','火铳铺').replace('英雄已应召','火枪已上架，等待居民领取').replace('未招募，已退还 8 金币','未出货，已退还 4 金币').replace('：8 金币','：4 金币').replace('refunded 8 coins','refunded 4 coins').replace('price=8','price=4').replace('assumed eight coins','assumed four coins').replace('[HeroShop]','[MusketeerShop]')
extra='''
    private static readonly System.Collections.Generic.List<DroppableTool> Guns = new();
    private static DroppableTool FindNativeBow()
    {
        var all = Resources.LoadAll<DroppableTool>("");
        for (int i = 0; all != null && i < all.Length; i++)
        {
            var tool = all[i];
            if (tool == null || tool.gameObject == null || tool.tag != "Bow") continue;
            var persistent = tool.GetComponent<Persistent>();
            if (persistent == null || string.IsNullOrEmpty(persistent.path)) continue;
            var manager = Managers.Inst?.pools;
            if (manager != null && manager.GetPoolByPrefabName(tool.gameObject.name) != null) return tool;
        }
        return null;
    }
    private static int RackCount()
    {
        if (_object == null) return 3;
        MusketeerIdentity.CopyGuns(Guns);
        int count = 0;
        foreach (var gun in Guns)
            if (gun != null && !gun.pickedUp && MusketeerAccess.InWorld(gun)
                && Math.Abs(gun.transform.position.x - _object.transform.position.x) < 1.7f) count++;
        return count;
    }
    private static bool TryCreateGun(out string reason)
    {
        reason = "枪具尚未就绪";
        if (!MusketeerIdentity.CanPurchase || _object == null || _layer == null || RackCount() >= 3) return false;
        DroppableTool prefab = FindNativeBow();
        if (prefab == null) return false;
        // Spawn from the real native Bow pool, retaining origin and the valid persistent path.
        // The shop itself is transient; the paid gun is parented directly beneath GameLayer.
        var position = _object.transform.position + new Vector3(-0.9f + 0.9f * RackCount(), 0.55f, -0.002f);
        DroppableTool gun = null;
        bool marked = false;
        try
        {
            gun = Pool.Spawn<DroppableTool>(prefab, position, Quaternion.identity, _layer, true);
            if (gun == null || gun.gameObject == null) return false;
            gun.transform.SetParent(_layer, true);
            gun.transform.position = position;
            gun.dropper = null; gun.parentShopRef = null;
            gun.selfDestruct = false;
            gun.pickUpPolicy = PickUpPolicy.Anybody;
            gun.pickedUp = false; gun.SetFake(false);
            if (gun._rigidbody != null) { gun._rigidbody.velocity = Vector2.zero; gun._rigidbody.angularVelocity = 0f; gun._rigidbody.isKinematic = true; }
            marked = MusketeerIdentity.TryRegisterPaidGun(gun);
            if (!marked) { reason = "职业记录暂不可写"; return false; }
            reason = "";
            return true;
        }
        finally
        {
            if (!marked && gun != null && gun.gameObject != null)
            {
                MusketeerIdentity.ForgetUnpaidGun(gun);
                Pool.Despawn(gun.gameObject, true);
            }
        }
    }
'''
body=body.replace('    private static bool LoadArt()\n',extra+'\n    private static bool LoadArt()\n')
payment=src[src.index('internal sealed class HeroShopPayment'):src.index('internal static class HeroShopPlacement')]
payment=payment[:payment.rfind('/// <summary>')].replace('HeroShopPayment','MusketeerShopPayment').replace('coins != 8','coins != 4')
header='using System;\nusing System.IO;\nusing System.Reflection;\nusing Il2CppInterop.Runtime.Injection;\nusing Il2CppInterop.Runtime.Attributes;\nusing UnityEngine;\n\nnamespace KingdomEnhancedMod;\n\n'
(root/'il2cpp/MusketeerShop.cs').write_text(header+payment+'\n'+body,encoding='utf8')
print('Generated distinct musketeer owner using audited native payment/retention lifecycle.')
