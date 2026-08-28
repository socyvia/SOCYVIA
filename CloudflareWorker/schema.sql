CREATE TABLE IF NOT EXISTS deployments (id TEXT PRIMARY KEY, public_id TEXT UNIQUE NOT NULL, package_key TEXT NOT NULL, package_hash TEXT NOT NULL, status TEXT NOT NULL, run_type TEXT NOT NULL DEFAULT 'Main', created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS deployment_conditions (deployment_id TEXT NOT NULL, condition_id TEXT NOT NULL, group_id TEXT, sort_order INTEGER NOT NULL, configuration_json TEXT, PRIMARY KEY(deployment_id, condition_id));
CREATE TABLE IF NOT EXISTS participants (id TEXT PRIMARY KEY, deployment_id TEXT NOT NULL, created_at TEXT NOT NULL, technical_metadata_json TEXT, pre_session_token TEXT, pre_questionnaire_completed_at TEXT);
CREATE TABLE IF NOT EXISTS sessions (id TEXT PRIMARY KEY, participant_id TEXT NOT NULL, deployment_id TEXT NOT NULL, condition_id TEXT NOT NULL, started_at TEXT, completed_at TEXT, completion_state TEXT NOT NULL, reconnect_count INTEGER NOT NULL DEFAULT 0, lifecycle_state TEXT NOT NULL DEFAULT 'SESSION_STARTED', feed_end_at TEXT, post_questionnaire_completed_at TEXT, run_type TEXT NOT NULL DEFAULT 'Main');
CREATE TABLE IF NOT EXISTS events (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, deployment_id TEXT NOT NULL, condition_id TEXT NOT NULL, content_id TEXT, event_type TEXT NOT NULL, client_timestamp TEXT NOT NULL, relative_ms INTEGER NOT NULL, payload_json TEXT, schema_version TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS questionnaire_responses (id TEXT PRIMARY KEY, session_id TEXT NOT NULL, questionnaire_version_id TEXT NOT NULL, response_json TEXT NOT NULL, submitted_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS sync_state (consumer_id TEXT PRIMARY KEY, cursor TEXT, updated_at TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS ix_events_session ON events(session_id);
CREATE INDEX IF NOT EXISTS ix_sessions_deployment ON sessions(deployment_id);
CREATE INDEX IF NOT EXISTS ix_sessions_lifecycle ON sessions(deployment_id, lifecycle_state, started_at);
CREATE INDEX IF NOT EXISTS ix_events_deployment_condition ON events(deployment_id, condition_id, event_type, relative_ms);
CREATE TABLE IF NOT EXISTS deployment_entry_config (deployment_id TEXT PRIMARY KEY NOT NULL, configuration_json TEXT NOT NULL, configuration_hash TEXT NOT NULL, schema_version TEXT NOT NULL, created_at TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS deployment_content (id TEXT PRIMARY KEY NOT NULL, deployment_id TEXT NOT NULL, condition_id TEXT, content_id TEXT NOT NULL, sort_order INTEGER NOT NULL, content_type TEXT NOT NULL, language TEXT NOT NULL, payload_json TEXT NOT NULL, interaction_config_json TEXT NOT NULL, configuration_hash TEXT NOT NULL, created_at TEXT NOT NULL, UNIQUE(deployment_id, condition_id, content_id));
CREATE INDEX IF NOT EXISTS ix_deployment_content_runtime ON deployment_content(deployment_id, condition_id, sort_order, content_id);
CREATE TABLE IF NOT EXISTS deployment_questionnaires (deployment_id TEXT NOT NULL, questionnaire_id TEXT NOT NULL, questionnaire_version_id TEXT NOT NULL, stage TEXT NOT NULL, definition_json TEXT NOT NULL, configuration_hash TEXT NOT NULL, schema_version TEXT NOT NULL, created_at TEXT NOT NULL, PRIMARY KEY(deployment_id, questionnaire_version_id), UNIQUE(deployment_id, stage));
CREATE TABLE IF NOT EXISTS participant_questionnaire_responses (id TEXT PRIMARY KEY NOT NULL, deployment_id TEXT NOT NULL, participant_id TEXT NOT NULL, session_id TEXT, questionnaire_id TEXT NOT NULL, questionnaire_version_id TEXT NOT NULL, stage TEXT NOT NULL, response_json TEXT NOT NULL, submitted_at TEXT NOT NULL, UNIQUE(participant_id, questionnaire_version_id));
CREATE INDEX IF NOT EXISTS ix_questionnaire_responses_session ON participant_questionnaire_responses(session_id);
CREATE INDEX IF NOT EXISTS ix_questionnaire_responses_participant ON participant_questionnaire_responses(participant_id, questionnaire_version_id);
CREATE INDEX IF NOT EXISTS ix_sessions_deployment_run_type ON sessions(deployment_id, run_type);
CREATE TRIGGER IF NOT EXISTS sessions_inherit_deployment_run_type
AFTER INSERT ON sessions
BEGIN
  UPDATE sessions
  SET run_type = COALESCE((SELECT run_type FROM deployments WHERE id = NEW.deployment_id), 'Main')
  WHERE id = NEW.id;
END;
