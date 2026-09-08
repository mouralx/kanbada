-- One-time import of the original workspace documents. Runs inside the EF migration transaction.
INSERT INTO users(id,email,name,password_hash,created_at) SELECT id,email,name,password_hash,created_at FROM legacy_users;
INSERT INTO identities(provider,subject,user_id) SELECT provider,subject,user_id FROM legacy_identities;
INSERT INTO sessions(id,user_id,expires_at) SELECT id,user_id,expires_at FROM legacy_sessions;
INSERT INTO workspaces(id,owner_id,personal,name,icon,banner,banner_position,version,updated_at)
SELECT id,owner_id,personal,state->'workspace'->>'name',state->'workspace'->>'icon',state->'workspace'->>'banner',
(state->'workspace'->>'bannerPosition')::double precision,version,updated_at FROM legacy_workspaces;
INSERT INTO members(workspace_id,email,user_id,invite_token,name,initials,color,photo,position)
SELECT m.workspace_id,m.email,m.user_id,m.invite_token,j->>'name',COALESCE(j->>'initials',''),COALESCE(j->>'color',''),j->>'photo',n-1
FROM legacy_members m JOIN legacy_workspaces w ON w.id=m.workspace_id
CROSS JOIN LATERAL jsonb_array_elements(w.state->'members') WITH ORDINALITY a(j,n)
WHERE lower(j->>'email')=m.email;
INSERT INTO files(id,workspace_id,uploader_id,name,content_type,bytes,created_at) SELECT id,workspace_id,uploader_id,name,content_type,bytes,created_at FROM legacy_files;
INSERT INTO projects(workspace_id,id,name,color,position,description,archived,system) SELECT w.id,j->>'id',j->>'name',j->>'color',n-1,COALESCE(j->>'description',''),COALESCE((j->>'archived')::boolean,false),j->>'system' FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'projects') WITH ORDINALITY a(j,n);
INSERT INTO statuses(workspace_id,id,name,color,position,complete) SELECT w.id,j->>'id',j->>'name',j->>'color',n-1,COALESCE((j->>'complete')::boolean,false) FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'statuses') WITH ORDINALITY a(j,n);
INSERT INTO buckets(workspace_id,id,name,color,position,complete) SELECT w.id,j->>'id',j->>'name',j->>'color',n-1,COALESCE((j->>'complete')::boolean,false) FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'buckets') WITH ORDINALITY a(j,n);
INSERT INTO labels(workspace_id,id,name,color,position,complete) SELECT w.id,j->>'id',j->>'name',j->>'color',n-1,COALESCE((j->>'complete')::boolean,false) FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'labels') WITH ORDINALITY a(j,n);
INSERT INTO swimlanes(workspace_id,id,name,color,position,complete,project_id) SELECT w.id,j->>'id',j->>'name',j->>'color',n-1,COALESCE((j->>'complete')::boolean,false),j->>'project' FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'swimlanes') WITH ORDINALITY a(j,n);
INSERT INTO cards(workspace_id,id,project_id,status_id,bucket_id,swimlane_id,title,description,priority,due,cover,position)
SELECT w.id,j->>'id',j->>'project',
(SELECT id FROM statuses WHERE workspace_id=w.id AND name=j->>'status'),
(SELECT id FROM buckets WHERE workspace_id=w.id AND name=NULLIF(j->>'bucket','')),
(SELECT id FROM swimlanes WHERE workspace_id=w.id AND project_id=j->>'project' AND name=NULLIF(j->>'swimlane','')),
j->>'title',COALESCE(j->>'description',''),j->>'priority',NULLIF(j->>'due','')::date,j->>'cover',n-1
FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') WITH ORDINALITY a(j,n);
INSERT INTO card_labels(workspace_id,card_id,position,label_id) SELECT w.id,j->>'id',n-1,(SELECT id FROM labels WHERE workspace_id=w.id AND name=x #>> '{}') FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'labels','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO card_assignees(workspace_id,card_id,position,member_email) SELECT w.id,j->>'id',n-1,(SELECT email FROM members WHERE workspace_id=w.id AND name=x #>> '{}') FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'assignees','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO card_attachments(workspace_id,card_id,position,file_id) SELECT w.id,j->>'id',n-1,(x->>'id')::uuid FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'attachments','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO card_comments(workspace_id,card_id,position,text) SELECT w.id,j->>'id',n-1,x #>> '{}' FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'comments','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO checklist_items(workspace_id,card_id,position,text,done) SELECT w.id,j->>'id',n-1,x->>'text',(x->>'done')::boolean FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'checklist','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO card_history(workspace_id,card_id,position,id,at,actor) SELECT w.id,j->>'id',n-1,x->>'id',(x->>'at')::timestamptz,x->>'actor' FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j) CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'history','[]'::jsonb)) WITH ORDINALITY a(x,n);
INSERT INTO history_changes(workspace_id,card_id,history_id,position,text)
SELECT w.id,j->>'id',h->>'id',n-1,x #>> '{}' FROM legacy_workspaces w
CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j)
CROSS JOIN LATERAL jsonb_array_elements(COALESCE(j->'history','[]'::jsonb)) history(h)
CROSS JOIN LATERAL jsonb_array_elements(h->'changes') WITH ORDINALITY a(x,n);
INSERT INTO workspace_activity(workspace_id,position,text)
SELECT w.id,n-1,j #>> '{}' FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'activity') WITH ORDINALITY a(j,n);
INSERT INTO notifications(workspace_id,id,message,at,position)
SELECT w.id,j->>'id',j->>'message',j->>'at',n-1 FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'notifications') WITH ORDINALITY a(j,n);
INSERT INTO shares(token,workspace_id,card_id,creator_id,access,expires_at,created_at) SELECT token,workspace_id,card_id,creator_id,access,expires_at,created_at FROM legacy_shares;

-- Reject an inconsistent legacy snapshot instead of silently discarding relationships.
DO $$ BEGIN
    IF (SELECT count(*) FROM members) <> (SELECT count(*) FROM legacy_members)
       OR (SELECT count(*) FROM members) <> (SELECT COALESCE(sum(jsonb_array_length(state->'members')),0) FROM legacy_workspaces)
       OR EXISTS (
           SELECT 1 FROM legacy_workspaces w CROSS JOIN LATERAL jsonb_array_elements(w.state->'tasks') t(j)
           JOIN cards c ON c.workspace_id=w.id AND c.id=j->>'id'
           WHERE (NULLIF(j->>'bucket','') IS NOT NULL AND c.bucket_id IS NULL)
              OR (NULLIF(j->>'swimlane','') IS NOT NULL AND c.swimlane_id IS NULL)
       ) THEN RAISE EXCEPTION 'Legacy relationships are inconsistent; migration rolled back. Repair the source data before retrying.';
    END IF;
END $$;
DROP TABLE legacy_shares;
DROP TABLE legacy_files;
DROP TABLE legacy_members;
DROP TABLE legacy_sessions;
DROP TABLE legacy_identities;
DROP TABLE legacy_workspaces;
DROP TABLE legacy_users;
DROP TABLE schema_versions;
