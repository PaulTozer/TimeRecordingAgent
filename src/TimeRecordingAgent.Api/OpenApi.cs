namespace TimeRecordingAgent.Api;

public static class OpenApi
{
    public static string Schema => """
{
  "openapi": "3.0.3",
  "info": {
    "title": "Timesheet API",
    "description": "API for retrieving and verifying timesheet entries. Use this API to get timesheet data for compliance checking.",
    "version": "1.0.0"
  },
  "servers": [
    {
      "url": "https://timerecording-api.nicesea-10808beb.swedencentral.azurecontainerapps.io"
    }
  ],
  "paths": {
    "/api/timesheets/{userId}/{date}": {
      "get": {
        "operationId": "getTimesheetEntries",
        "summary": "Get timesheet entries for a specific date",
        "description": "Retrieves all timesheet entries for a user on a specific date. Use this to review entries for compliance.",
        "parameters": [
          {
            "name": "userId",
            "in": "path",
            "required": true,
            "description": "The user's email address",
            "schema": {
              "type": "string"
            }
          },
          {
            "name": "date",
            "in": "path",
            "required": true,
            "description": "The date to get entries for (format: yyyy-MM-dd)",
            "schema": {
              "type": "string",
              "format": "date"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "List of timesheet entries",
            "content": {
              "application/json": {
                "schema": {
                  "type": "array",
                  "items": {
                    "$ref": "#/components/schemas/TimesheetEntry"
                  }
                }
              }
            }
          }
        }
      }
    },
    "/api/timesheets/{userId}/unverified": {
      "get": {
        "operationId": "getUnverifiedEntries",
        "summary": "Get unverified timesheet entries",
        "description": "Retrieves timesheet entries that haven't been verified yet. Use this to find entries needing compliance review.",
        "parameters": [
          {
            "name": "userId",
            "in": "path",
            "required": true,
            "description": "The user's email address",
            "schema": {
              "type": "string"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "List of unverified timesheet entries",
            "content": {
              "application/json": {
                "schema": {
                  "type": "array",
                  "items": {
                    "$ref": "#/components/schemas/TimesheetEntry"
                  }
                }
              }
            }
          }
        }
      }
    },
    "/api/timesheets/{userId}/summary": {
      "get": {
        "operationId": "getTimesheetSummary",
        "summary": "Get timesheet summary statistics",
        "description": "Retrieves summary statistics for a user's timesheet entries over a date range.",
        "parameters": [
          {
            "name": "userId",
            "in": "path",
            "required": true,
            "description": "The user's email address",
            "schema": {
              "type": "string"
            }
          },
          {
            "name": "startDate",
            "in": "query",
            "required": false,
            "description": "Start date (format: yyyy-MM-dd). Defaults to 7 days ago.",
            "schema": {
              "type": "string",
              "format": "date"
            }
          },
          {
            "name": "endDate",
            "in": "query",
            "required": false,
            "description": "End date (format: yyyy-MM-dd). Defaults to today.",
            "schema": {
              "type": "string",
              "format": "date"
            }
          }
        ],
        "responses": {
          "200": {
            "description": "Summary statistics",
            "content": {
              "application/json": {
                "schema": {
                  "$ref": "#/components/schemas/TimesheetSummary"
                }
              }
            }
          }
        }
      }
    },
    "/api/timesheets/verify/{entryId}": {
      "post": {
        "operationId": "updateVerificationStatus",
        "summary": "Update verification status of an entry",
        "description": "Updates the compliance verification status of a timesheet entry after review.",
        "parameters": [
          {
            "name": "entryId",
            "in": "path",
            "required": true,
            "description": "The ID of the timesheet entry to update",
            "schema": {
              "type": "integer",
              "format": "int64"
            }
          }
        ],
        "requestBody": {
          "required": true,
          "content": {
            "application/json": {
              "schema": {
                "$ref": "#/components/schemas/VerificationUpdate"
              }
            }
          }
        },
        "responses": {
          "200": {
            "description": "Verification status updated successfully"
          }
        }
      }
    }
  },
  "components": {
    "schemas": {
      "TimesheetEntry": {
        "type": "object",
        "properties": {
          "id": {
            "type": "integer",
            "format": "int64",
            "description": "Unique identifier for the entry"
          },
          "userId": {
            "type": "string",
            "description": "The user's email address"
          },
          "startedAt": {
            "type": "string",
            "format": "date-time",
            "description": "When the work started"
          },
          "endedAt": {
            "type": "string",
            "format": "date-time",
            "description": "When the work ended"
          },
          "durationHours": {
            "type": "number",
            "format": "decimal",
            "description": "Duration in hours"
          },
          "processName": {
            "type": "string",
            "description": "The application used (e.g., WINWORD, OUTLOOK)"
          },
          "documentName": {
            "type": "string",
            "description": "Name of the document or email being worked on"
          },
          "groupName": {
            "type": "string",
            "nullable": true,
            "description": "Optional grouping/matter name"
          },
          "isBillable": {
            "type": "boolean",
            "description": "Whether this entry is billable"
          },
          "billableCategory": {
            "type": "string",
            "nullable": true,
            "description": "The billing category (e.g., Research, Drafting, Review)"
          },
          "description": {
            "type": "string",
            "nullable": true,
            "description": "Description of the work performed"
          },
          "isApproved": {
            "type": "boolean",
            "description": "Whether the user has approved this entry"
          },
          "verificationStatus": {
            "type": "string",
            "nullable": true,
            "description": "Compliance status: null (not reviewed), Compliant, NeedsReview, or NonCompliant"
          },
          "verificationNotes": {
            "type": "string",
            "nullable": true,
            "description": "Notes from compliance review"
          }
        }
      },
      "VerificationUpdate": {
        "type": "object",
        "required": ["status"],
        "properties": {
          "status": {
            "type": "string",
            "description": "The verification status: Compliant, NeedsReview, or NonCompliant"
          },
          "notes": {
            "type": "string",
            "nullable": true,
            "description": "Optional notes explaining the verification decision"
          }
        }
      },
      "TimesheetSummary": {
        "type": "object",
        "properties": {
          "userId": {
            "type": "string"
          },
          "startDate": {
            "type": "string",
            "format": "date"
          },
          "endDate": {
            "type": "string",
            "format": "date"
          },
          "totalEntries": {
            "type": "integer",
            "format": "int64"
          },
          "totalHours": {
            "type": "number",
            "format": "decimal"
          },
          "billableHours": {
            "type": "number",
            "format": "decimal"
          },
          "compliantCount": {
            "type": "integer",
            "format": "int64"
          },
          "nonCompliantCount": {
            "type": "integer",
            "format": "int64"
          },
          "pendingCount": {
            "type": "integer",
            "format": "int64"
          }
        }
      }
    }
  }
}
""";
}
