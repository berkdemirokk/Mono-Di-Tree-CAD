# V2 model-only requests

A V2 design request with a supported model ID may have an empty or whitespace-only description and no image, with or without measurement notes. The ID is content, not a claim that measurements are sufficient or a pattern has been made. V1 and V2 requests with no model ID still require a description or reference image. No implicit text or V1 conversion is added.

Compatibility: older Windows and iPhone builds that require a description or image for every request reject these newly valid V2 files, even though the JSON schema itself is unchanged. Update both applications before exchanging such files; do not silently downgrade to V1, which cannot preserve a V2 model identity.
