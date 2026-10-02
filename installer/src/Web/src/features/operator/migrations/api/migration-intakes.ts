export type SecureMigrationIntake = { intakeId:string; displayName:string; status:string; ageRecipient:string; recipientFingerprint:string; createdAtUtc:string; expiresAtUtc:string; packageFileName?:string|null; packageSizeBytes?:number|null; encryptedPackageSha256?:string|null; decryptedArchiveSha256?:string|null; packageUploadedAtUtc?:string|null; packageValidatedAtUtc?:string|null; archiveMigrationId?:string|null; archiveSourceProduct?:string|null; archiveSourceVersion?:string|null; archiveStackCount?:number|null }

export type MigrationSourceRequestPurpose = "preview" | "final"

export type MigrationSourceRequestDownload = {
  blob: Blob
  fileName: string
}

export type MigrationFinalPackageUpload = {
  migrationId: string
  packageRevisionId: string
  revisionNumber: number
  status: string
  packageFileName: string
  packageSizeBytes: number
  encryptedPackageSha256: string
  decryptedArchiveSha256: string
  archiveMigrationId: string
  startSourceFingerprint: string
  completionSourceFingerprint: string
  archiveStackCount: number
  captureKind: string
  sourceFrozen: boolean
  rehearsalOnly: boolean
  validatedAtUtc: string
  authoritySelected: boolean
}

export type MigrationFinalPackageRecipient = {
  migrationId: string
  packageRevisionId: string
  revisionNumber: number
  purpose: string
  status: string
  ageRecipient: string
  recipientFingerprint: string
  createdAtUtc: string
  expiresAtUtc: string
  resumedExisting: boolean
}

const secureBase="/api/operator/migrations/secure-intakes"

export class SecureMigrationIntakeProblemError extends Error {
  readonly status:number
  readonly code:string|null
  constructor(message:string,status:number,code:string|null=null){super(message);this.name="SecureMigrationIntakeProblemError";this.status=status;this.code=code}
}

export function isSecureMigrationStepUpRequired(value:unknown):boolean{
  return value instanceof SecureMigrationIntakeProblemError&&value.status===403&&value.code==="step_up_required"
}

async function secureRequest(path:string,body?:unknown):Promise<SecureMigrationIntake>{
  const response=await fetch(path,{method:"POST",credentials:"include",cache:"no-store",headers:{Accept:"application/json","Content-Type":"application/json"},body:body===undefined?undefined:JSON.stringify(body)})
  if(!response.ok){let code:string|null=null;try{const problem=await response.json() as {status?:unknown;code?:unknown};code=typeof problem.status==="string"?problem.status:typeof problem.code==="string"?problem.code:null}catch{code=null}throw new SecureMigrationIntakeProblemError(`POST ${path} failed with status ${response.status}${code?`: ${code}`:""}`,response.status,code)}
  return await response.json() as SecureMigrationIntake
}

export const createSecureMigrationIntake=(displayName:string)=>secureRequest(secureBase,{displayName})

export async function uploadSecureMigrationPackage(id:string,file:File):Promise<SecureMigrationIntake>{
  const form=new FormData();form.append("package",file)
  const response=await fetch(`${secureBase}/${encodeURIComponent(id)}/package`,{method:"POST",credentials:"include",cache:"no-store",headers:{Accept:"application/json"},body:form})
  if(!response.ok){let code:string|null=null;let message=`Package upload failed with status ${response.status}`;try{const problem=await response.json() as {status?:unknown;code?:unknown;message?:unknown};code=typeof problem.status==="string"?problem.status:typeof problem.code==="string"?problem.code:null;if(typeof problem.message==="string")message=problem.message}catch{}throw new SecureMigrationIntakeProblemError(message,response.status,code)}
  return await response.json() as SecureMigrationIntake
}


export async function createFinalPackageRecipient(
  migrationId:string,
):Promise<MigrationFinalPackageRecipient>{
  const path=`/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/package-revisions/final-recipient`
  const response=await fetch(path,{method:"POST",credentials:"include",cache:"no-store",headers:{Accept:"application/json"}})
  if(!response.ok){let code:string|null=null;let message=`Final package recipient request failed with status ${response.status}`;try{const problem=await response.json() as {status?:unknown;code?:unknown;message?:unknown};code=typeof problem.status==="string"?problem.status:typeof problem.code==="string"?problem.code:null;if(typeof problem.message==="string")message=problem.message}catch{}throw new SecureMigrationIntakeProblemError(message,response.status,code)}
  return await response.json() as MigrationFinalPackageRecipient
}

export async function uploadFinalMigrationPackage(
  migrationId:string,
  file:File,
):Promise<MigrationFinalPackageUpload>{
  const form=new FormData()
  form.append("package",file)
  const path=`/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/package-revisions/final/package`
  const response=await fetch(path,{method:"POST",credentials:"include",cache:"no-store",headers:{Accept:"application/json"},body:form})
  if(!response.ok){let code:string|null=null;let message=`Final package upload failed with status ${response.status}`;try{const problem=await response.json() as {status?:unknown;code?:unknown;message?:unknown};code=typeof problem.status==="string"?problem.status:typeof problem.code==="string"?problem.code:null;if(typeof problem.message==="string")message=problem.message}catch{}throw new SecureMigrationIntakeProblemError(message,response.status,code)}
  return await response.json() as MigrationFinalPackageUpload
}


function migrationSourceRequestFileName(response:Response,purpose:MigrationSourceRequestPurpose){
  const disposition=response.headers.get("Content-Disposition")??""
  const encoded=/filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
  if(encoded){try{return decodeURIComponent(encoded)}catch{/* fall through */}}
  const basic=/filename="?([^";]+)"?/i.exec(disposition)?.[1]?.trim()
  return basic||`mem-migration-request-${purpose}.json`
}

export async function downloadMigrationSourceRequest(
  migrationId:string,
  purpose:MigrationSourceRequestPurpose,
):Promise<MigrationSourceRequestDownload>{
  const path=`/api/operator/migrations/sessions/${encodeURIComponent(migrationId)}/package-revisions/${purpose}/source-request`
  const response=await fetch(path,{method:"GET",credentials:"include",cache:"no-store",headers:{Accept:"application/json"}})
  if(!response.ok){let code:string|null=null;let message=`Migration request download failed with status ${response.status}`;try{const problem=await response.json() as {code?:unknown;message?:unknown};code=typeof problem.code==="string"?problem.code:null;if(typeof problem.message==="string")message=problem.message}catch{}throw new SecureMigrationIntakeProblemError(message,response.status,code)}
  return {blob:await response.blob(),fileName:migrationSourceRequestFileName(response,purpose)}
}
