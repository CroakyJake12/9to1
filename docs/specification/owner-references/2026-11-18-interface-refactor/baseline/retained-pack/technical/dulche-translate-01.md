# Dulche Translate service — Translate; Translation Sets and variants (part 01)

Reading status: active retained clauses, subject to the v3 owner overrides listed in the primary module. Historical process instructions and replaced original wording are stored separately.

### V2-B1826 · v2 lines 10815–10815

Translate

### V2-B1827 · v2 lines 10818–10818

Translate is a first-class Dulche framework capability for multilingual translation, localisation and cross-language interpretation. It is not owned by a standalone Translate app. The primary user-facing product is the built-in Translate Space, while Write, Mail, Boards, Browse, Vision & Voice, AI Overlay, Sites and other authorised surfaces may invoke Dulche Translate directly for contextual translation.

### V2-B1828 · v2 lines 10821–10821

Dulche Translate owns language/locale resolution, translation execution, multi-target Translation Sets, variant identity, glossary/protected-term application, segment alignment, structured-artifact translation contracts, translation job lifecycle, provenance and translation-specific recovery. It MUST use the shared model/provider layer and normal Home/Dulche permission and audit contracts.

### V2-B1829 · v2 lines 10824–10824

Translation Sets and variants

### V2-B1830 · v2 lines 10827–10827

A translation request MAY target one or many languages/locales in a single operation. The canonical result is a TranslationSet with stable TranslationSetID, source reference/revision where applicable, source language/locale, request/instruction metadata, glossary references, model policy, created/modified timestamps and one or more TranslationVariants.

### V2-B1831 · v2 lines 10830–10830

Every TranslationVariant MUST have stable TranslationVariantID, TranslationSetID, target language/locale, status, translated content or canonical output reference, revision, provenance, errors/warnings and segment map where available. Retrying, regenerating, manually editing, applying or publishing one language MUST NOT discard successful sibling variants.

### V2-B1832 · v2 lines 10833–10833

Language identity MUST use canonical language/locale identifiers. When a user supplies an ambiguous or unsupported language label, Dulche Translate MUST request/return a structured resolution requirement rather than silently guessing another language.

### V2-B1833 · v2 lines 10836–10836

Conversational Translate Space rendering

### V2-B1834 · v2 lines 10839–10839

The built-in Translate Space renders translation work as normal conversation rather than a two-box translator. The selected target-language chips sit immediately above the standard Space chatbar; all source/context selection uses the normal Spaces context architecture.

### V2-B1835 · v2 lines 10842–10842

When one conversational request returns multiple TranslationVariants, each language variant MUST appear as its own assistant speech bubble. The language/locale name is displayed directly above that bubble. The bubbles are linked as siblings by the same TranslationSetID/request but remain independently addressable for retry, regenerate, edit, apply, export or agentic follow-up.

### V2-B1836 · v2 lines 10845–10845

A prompt may temporarily target a subset/additional language without necessarily changing the Space's persistent language chips. Explicit requests to add/remove/change the default language set update the Translate Space configuration.

### V2-B1837 · v2 lines 10848–10848

Conversational refinement and agentic actions

### V2-B1838 · v2 lines 10851–10851

Translate MUST support natural follow-up refinement such as 'make the German version less formal', 'keep Action Graph untranslated everywhere', 'add Japanese', 'only redo French', or 'update these because the source changed'. Refinements target the canonical TranslationSet/variant rather than creating unrelated copies unless the user requests a separate result.

### V2-B1839 · v2 lines 10854–10854

Translate Space MAY perform multi-step agentic localisation workflows using normal Space Plugins, Skills, persistent Dulche Agents, Automations and typed app actions. For example, 'translate this page into French, Spanish, German and Italian, then push them to the live site' may resolve the current page semantically, create/update four TranslationVariants, validate each owning artifact/site route and invoke the authorised Sites/WordPress/publishing APIs.

### V2-B1840 · v2 lines 10857–10857

Dulche Translate MUST NOT implement generic hidden publishing or file mutation that bypasses owning apps. Applying translations to Write/Present/Data/Boards/Sites/WordPress/GitHub or other systems uses those systems' canonical typed actions, revision checks, permissions, approvals, audit and idempotency contracts. Partial downstream failure MUST preserve already-successful translations/actions and expose targeted retry.

### V2-B1841 · v2 lines 10860–10860

Structured artifacts and semantic preservation

### V2-B1842 · v2 lines 10863–10863

Dulche Translate MUST translate human-language content while preserving supported non-linguistic structure and identity. For 9to1-native artifacts it should operate through the owning app/shared Productivity Engine using stable object identities rather than flattening the artifact into plain text.

### V2-B1843 · v2 lines 10866–10866

Write translation preserves document structure/styles/tables/images/links/comments and other supported metadata while translating intended text. Present translation preserves slides/layout/theme/text boxes/speaker notes/animations/transitions and detects translated-text overflow instead of silently clipping. Data translation distinguishes human labels/descriptions/comments from formulas, numbers, cell references and identifiers. Boards translation preserves page/block identity and supported embedded artifact references.

### V2-B1844 · v2 lines 10869–10869

Code, commands, URLs, formulas, JSON keys, identifiers, markup/code fences and other structured technical content MUST follow explicit structure-aware rules and MUST NOT be casually translated as prose. The user may explicitly request translation of translatable string literals/comments where the owning format supports safe editing.

### V2-B1845 · v2 lines 10872–10872

Whole-artifact translation SHOULD create a translated derivative/locale variant by default rather than destructively overwrite the source. Explicit in-place commands such as Replace selection with translation use the owning app's normal undo/revision semantics.

### V2-B1846 · v2 lines 10875–10875

Segments, provenance and incremental updates

### V2-B1847 · v2 lines 10878–10878

Where the source supports stable segmentation, Dulche Translate SHOULD preserve segment identity/alignment between source and each target variant. Segment maps enable side-by-side review, targeted retry, terminology correction and incremental updates.

### V2-B1848 · v2 lines 10881–10881

Translated derivatives MUST retain provenance sufficient to identify source artifact/file/entity, source revision, source/target language, TranslationSetID/VariantID, glossary IDs, model policy and translation time. When the source changes, Translate MUST be able to report that a translation is stale.

### V2-B1849 · v2 lines 10884–10884

Where safe segment identity exists, updating a stale translation SHOULD retranslate only changed/affected source segments while preserving unaffected translated content and user edits. Conflicts between manual target edits and changed source content MUST be surfaced rather than silently overwritten.

### V2-B1850 · v2 lines 10887–10887

Glossaries and protected terminology

### V2-B1851 · v2 lines 10890–10890

Dulche Translate MUST support reusable and request-scoped glossaries. Glossary entries MAY define source term, required/preferred target translation by locale, Do Not Translate, notes/context and applicability scope.

### V2-B1852 · v2 lines 10893–10893

Protected terminology MUST support names, trademarks, product names, case names, technical identifiers, commands and other content that should remain unchanged. Glossary/protected-term application MUST be recorded in translation provenance so unexpected terminology can be diagnosed.

### V2-B1853 · v2 lines 10896–10896

Text, image, document and media translation

### V2-B1854 · v2 lines 10899–10899

Dulche Translate MUST support direct text and semantic selection translation. Image/screenshot translation MAY return extracted translated text and, where the selected model/tooling supports it, a translated visual derivative with mapped text regions. Semantic text from a 9to1 surface is preferred over OCR/pixels when available.

### V2-B1855 · v2 lines 10902–10902

PDF handling MUST distinguish text, scanned and mixed PDFs where detectable. Structured text PDFs should preserve layout/structure as far as supported; scanned/mixed PDFs may require vision/OCR-style extraction. Any layout fidelity loss or uncertain extraction MUST be surfaced before export/apply.

### V2-B1856 · v2 lines 10905–10905

Audio translation may produce translated text and, where supported, translated speech. Video translation MAY produce translated transcript/subtitles and advanced dubbed output where supported. Live realtime interpretation is exposed through Vision & Voice → Live Translate while reusing Dulche Translate language/context/glossary functionality.

### V2-B1857 · v2 lines 10908–10908

Model routing and privacy

### V2-B1858 · v2 lines 10911–10911

Dulche Translate uses the shared Universal Model Picker/model-routing system. Ordinary translation requests SHOULD resolve the highest-quality compatible configured route for the requested languages, modality, context size, latency, privacy/locality, entitlement/cost and tool requirements without forcing the user to manually choose a model.

### V2-B1859 · v2 lines 10914–10914

An explicit local-only/private policy MUST NOT silently fall back to cloud translation. Unknown language-pair/provider capability is represented as unavailable/unknown rather than fabricated support.

### V2-B1860 · v2 lines 10917–10917

Translation jobs and recovery

### V2-B1861 · v2 lines 10920–10920

Long or multi-artifact translation MUST use durable TranslationJob state with stable TranslationJobID and statuses such as Preparing, Translating, ReviewAvailable, Applying/Exporting, Completed, Paused, Failed and Cancelled as applicable. Progress MUST derive from real segments/pages/slides/files/variants rather than invented percentages.

### V2-B1862 · v2 lines 10923–10923

Partial failure MUST preserve successful completed segments/variants. A failed page/language/action can be retried independently. Pause/resume/retry MUST not repeat already-completed consequential downstream actions without idempotency/operation identity.

### V2-B1863 · v2 lines 10926–10926

Canonical Dulche Translate API

### V2-B1864 · v2 lines 10929–10951

Dulche.Translate.DetectLanguage(input, options?);
Dulche.Translate.Text(input, targetLanguages, options?);
Dulche.Translate.CreateSet(source, targetLanguages, options?);
Dulche.Translate.GetSet(translationSetID);
Dulche.Translate.AddLanguages(translationSetID, targetLanguages);
Dulche.Translate.RemoveLanguage(translationSetID, targetLanguage);
Dulche.Translate.RefineVariant(translationVariantID, instruction);
Dulche.Translate.RegenerateVariant(translationVariantID, options?);
Dulche.Translate.TranslateArtifact(sourceRef, targetLanguages, options?);
Dulche.Translate.TranslateImage(source, targetLanguages, options?);
Dulche.Translate.TranslateAudio(source, targetLanguages, options?);
Dulche.Translate.CreateJob(config);
Dulche.Translate.GetJob(translationJobID);
Dulche.Translate.PauseJob(translationJobID);
Dulche.Translate.ResumeJob(translationJobID);
Dulche.Translate.CancelJob(translationJobID);
Dulche.Translate.Retry(translationJobID, target?);
Dulche.Translate.Glossary.List();
Dulche.Translate.Glossary.Get(glossaryID);
Dulche.Translate.Glossary.Create(definition);
Dulche.Translate.Glossary.Update(glossaryID, patch, expectedRevision?);
Dulche.Translate.GetSegmentMap(translationVariantID);
Dulche.Translate.CheckSourceRevision(translationSetID);

### V2-B1865 · v2 lines 10954–10954

These are minimum canonical entry points. Applying/publishing translated outputs remains the responsibility of the owning app/Plugin APIs; Dulche Translate returns typed TranslationSet/Variant/artifact references and orchestration metadata rather than bypassing those owners.

### V2-B1866 · v2 lines 10957–10957

Failure behaviour

### V2-B1867 · v2 lines 10960–10960

Dulche Translate MUST return structured failures including LanguageUnsupported, LanguageAmbiguous, LocaleUnsupported, TranslationModelUnavailable, TranslationCapabilityUnavailable, GlossaryUnavailable, SourceUnavailable, SourceRevisionConflict, SegmentAlignmentUnavailable, VariantNotFound, TranslationSetNotFound, TranslationJobNotFound, TranslationJobNotResumable, StructuredFormatUnsupported, LayoutPreservationUnavailable, PartialTranslationFailure, PrivacyPolicyDenied and ProviderUnavailable in addition to normal Dulche/tool/action failures.

### V2-B1868 · v2 lines 10963–10963

Acceptance requirements

### V2-B1869 · v2 lines 10966–10980

Dulche Translate is implementation-complete only when acceptance coverage verifies at least:
* One request can translate into multiple target languages and returns independently addressable TranslationVariants under one TranslationSetID.
* Translate Space places language chips immediately above the normal chatbar and uses normal Space context rather than a separate context row.
* Each conversational target-language translation renders in its own assistant speech bubble with the language/locale name above it.
* Retry/regenerate/edit of one language does not discard successful sibling language variants.
* A conversational request can perform authorised downstream actions such as applying/publishing translated variants only through owning typed APIs.
* A partial downstream publish/apply failure preserves successful languages and supports targeted retry.
* Write/Present/Data/Boards translations preserve supported structure and do not flatten artifacts into plain text.
* Formula/code/identifier content is protected from inappropriate prose translation.
* Glossaries and Do Not Translate terms apply consistently and remain attributable.
* Source revision changes are detected and segment-aware incremental update preserves unaffected/manual target content where safe.
* Image/PDF/media translation reports extraction/layout limitations truthfully.
* Local-only/privacy policy cannot silently route translation to cloud.
* Long/multi-artifact jobs preserve successful progress across pause/failure/retry.
* Vision & Voice Live Translate reuses this framework rather than implementing a separate translation engine.
