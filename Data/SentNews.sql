CREATE TABLE "SentNews" (
    "Id" SERIAL PRIMARY KEY,
    "Title" TEXT NOT NULL,
   "Link" TEXT,
  "Hash" TEXT,
    "SentDate" TIMESTAMP WITHOUT TIME ZONE NOT NULL
);

CREATE UNIQUE INDEX "IX_SentNews_Title"
ON "SentNews" ("Title");

CREATE INDEX "IX_SentNews_SentDate"
ON "SentNews" ("SentDate");