"""Inventory canonical entry points against actual baseline contract members.

No HTTP path or hosted implementation is inferred from a C# method declaration.
"""
import json
import re
from pathlib import Path

BASE=Path(__file__).resolve().parent
ROOT=Path('/workspace/team-b-worktree')
rows=json.loads((BASE/'requirements.json').read_text())
contract_path=ROOT/'9to1 Workspace/Files/CUI/Contracts/HostedFilesContracts.cs'
contract=contract_path.read_text().split('public interface IFilesService',1)[1].split('public interface IFilesOwningAppRevisionSink',1)[0]
files_methods=set(re.findall(r'\b(\w+Async)\(',contract))
aliases={
 'Transfer.Get':'GetTransfer','Transfer.Pause':'PauseTransfer','Transfer.Resume':'ResumeTransfer','Transfer.Cancel':'CancelTransfer',
 'Sync.GetState':'GetSyncState','Sync.GetItemState':'GetItemSyncState','Sync.Now':'SyncNow','Sync.Item':'SyncItem','Sync.Folder':'SyncFolder','Sync.Pause':'PauseSync','Sync.Resume':'ResumeSync','Sync.SetAvailability':'SetAvailability','Sync.ListPending':'ListPendingOperations','Sync.ListConflicts':'ListConflicts','Sync.ResolveConflict':'ResolveConflict',
 'Share.Get':'GetGrants','Share.Grant':'GrantAccess','Share.Revoke':'RevokeAccess','Changes.Subscribe':'SubscribeChanges','Changes.GetSince':'GetChanges'}
sites={
 'CreateProject':('Application/SiteProjectService.cs','CreateProjectAsync'),
 'OpenProject':('Application/SiteProjectService.cs','GetProjectAsync'),
 'ReserveSlug':('Application/SitePublicIdentityService.cs','ReserveSlugAsync'),
 'ChangeSlug':('Application/SitePublicIdentityService.cs','ChangeSlugAsync'),
 'VerifyPublicName':('Application/SiteNameVerificationService.cs','VerifyAsync'),
 'GetPublicNameVerification':('Application/SiteNameVerificationService.cs','GetVerificationAsync'),
 'BeginDomainVerification':('Application/SitePublicIdentityService.cs','BeginDomainVerificationAsync'),
 'VerifyDomain':('Application/SitePublicIdentityService.cs','VerifyDomainAsync'),
 'AttachDomain':('Application/SitePublicIdentityService.cs','AttachDomainAsync'),
 'ConfigureDomainRecords':('Application/SitePublicIdentityService.cs','ConfigureDomainRecordsAsync'),
 'Deploy':('Hosting/SiteDeploymentService.cs','BuildAndDeployAsync'),
 'RollbackDeployment':('Hosting/SiteDeploymentService.cs','RollbackDeploymentAsync')}
out=[]
seen=set()
for row in rows:
 if row['product'] not in ['FILES','SITES']:continue
 for app,action,args in re.findall(r'9to1\.(Files|Sites)\.([\w.]+)\(([^\n;]*)\)',row['criterion']):
  if (app,action) in seen:continue
  seen.add((app,action))
  if app=='Files':
   member=aliases.get(action,action)+'Async';path=contract_path;exists=member in files_methods
   notes='Contract declaration only; no concrete IFilesService/hosted provider or real browser entry point.'
  else:
   path_rel,member=sites.get(action,('',None));path=ROOT/'9to1 Workspace/Sites'/path_rel if path_rel else None
   exists=bool(path and member and re.search(r'\b'+member+r'\(',path.read_text()))
   notes='Local service method candidate only; semantic UI/action schema and authenticated hosted route not established.'
   if action=='OpenProject':notes+=' GetProjectAsync retrieves data; it does not implement browser launch/navigation.'
   if action=='BeginDomainVerification':notes+=' Canonical signature uses siteID/domain; existing method uses domainBindingID after AttachDomain; adapter contract needs owning-team review.'
  out.append({'canonical_action':f'9to1.{app}.{action}','canonical_arguments':args,'source_requirement':row['requirement_id'],'source_index':row['source_index'],
   'browser_ui':'MISSING','domain_candidate':member if exists else None,'domain_source':str(path) if exists else None,
   'domain_member_found':exists,'typed_http_transport':'NOT_DEFINED/NOT_ACKNOWLEDGED','owning_team':'A shared domain; C4 hosted service; B4 browser surface',
   'tests':row['test_ids'],'outcome':'NOT_RUN','state':'IMPLEMENTED-UNVERIFIED' if exists else 'MISSING','notes':notes})
(BASE/'api-operation-map.json').write_text(json.dumps(out,indent=2)+'\n')
print(json.dumps({'canonical_api_operations':len(out),'domain_members_found':sum(r['domain_member_found'] for r in out),'real_browser_actions_verified':0},indent=2))
