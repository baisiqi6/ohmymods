# Independent review: v1 bug empty baseline and subsequent rollback

Reviewer hero_store_flags_review confirmed island realStartDateTime notserialized; campaignStart is intDayOfYear notunique.

After initial fixture007 proof, user bought again under7035 and saved7add in latestnative snapshot. Need this migrationsequence:
1. firstV2load7add ->7035 two NEW paidGUIDs.
2. rollback samecontextto007:7035 containsv1bugEMPTYexact, whileunclaimedlegacy651 containsPAIDexact. Mustrecover651paid and associateasotherepoch, preserving7035.
3. reload7add ->7035newpaidagain.

Therefore searchinglegacy onlyforunboundcontext is insufficient. Onknowncontext exactLEGACYEMPTY orunknown, stillsearchunclaimedlegacy nonemptyexact, excluding anyscopealreadyclaimedbyOTHERcontext. Allnonemptycandidates mustagree; nofirst/newestselection. Need perSNAPSHOT legacyV1 provenance (notjustscope, sincev2appends tolegacyscope). Decodev1markslegacy; persistedv2remembersorigin. V2known-authoritativeempty (confirmed newepoch generation or genuine fullyresolved native save afterdeath) mustNOT resurrectlegacypaid evenidenticalJSON. Merelyloading/ConfirmBaseline legacyempty shouldnotlaunder itintoauthoritativeV2empty. Only commitassociationafterload succeeds; failure keepsactive/history unchanged. Preservealloldscopes/snapshots andboundscapacityfailclosed.

These are correctnessnotes for review, notpermissiontoedit nativefiles. Fixtures may be refreshed under a distinctreceipts/latest-* filename byoperator; originalfixtures remainstable.
