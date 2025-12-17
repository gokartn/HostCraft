-- Drop default values first
ALTER TABLE "Applications" ALTER COLUMN "EnableHttps" DROP DEFAULT;
ALTER TABLE "Applications" ALTER COLUMN "ForceHttps" DROP DEFAULT;

-- Convert column types
ALTER TABLE "Applications" ALTER COLUMN "EnableHttps" TYPE boolean USING (CASE WHEN "EnableHttps" = 0 THEN false ELSE true END);
ALTER TABLE "Applications" ALTER COLUMN "ForceHttps" TYPE boolean USING (CASE WHEN "ForceHttps" = 0 THEN false ELSE true END);

-- Set empty ProxyDeployedAt to NULL first
UPDATE "Servers" SET "ProxyDeployedAt" = NULL WHERE "ProxyDeployedAt" = '' OR "ProxyDeployedAt" IS NULL;

-- Now convert to timestamp
ALTER TABLE "Servers" ALTER COLUMN "ProxyDeployedAt" TYPE timestamp USING 
  CASE 
    WHEN "ProxyDeployedAt" IS NULL OR "ProxyDeployedAt" = '' THEN NULL 
    ELSE "ProxyDeployedAt"::timestamp 
  END;

-- Restore default values as booleans
ALTER TABLE "Applications" ALTER COLUMN "EnableHttps" SET DEFAULT false;
ALTER TABLE "Applications" ALTER COLUMN "ForceHttps" SET DEFAULT false;
