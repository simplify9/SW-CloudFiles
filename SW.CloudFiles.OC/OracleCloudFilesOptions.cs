using System;
using SW.PrimitiveTypes;

namespace SW.CloudFiles.OC
{
    /// <summary>Configuration options for the Oracle Cloud Infrastructure Object Storage provider.</summary>
    public class OracleCloudFilesOptions : CloudFilesOptions
    {
        /// <summary>OCI tenancy OCID.</summary>
        public string TenantId { get; set; }

        /// <summary>API key fingerprint.</summary>
        public string FingerPrint { get; set; }

        /// <summary>OCI user OCID.</summary>
        public string UserId { get; set; }

        /// <summary>RSA private key (PEM format) used to sign API requests.</summary>
        public string RSAKey { get; set; }

        /// <summary>Path to the OCI config file written at startup. Set automatically by the Extensions registration.</summary>
        public string ConfigPath { get; set; }

        /// <summary>OCI region identifier (e.g. <c>us-ashburn-1</c>).</summary>
        public string Region { get; set; }

        /// <summary>Object Storage namespace for the tenancy.</summary>
        public string NamespaceName { get; set; }

        /// <summary>
        /// When true, skips automatic creation of temp-prefix lifecycle deletion rules on the bucket.
        /// Lifecycle rules are: temp1/ (1 day), temp7/ (7 days), temp30/ (30 days), temp365/ (365 days).
        /// </summary>
        public bool DisableAutoLifecycle { get; set; }

        internal string GetFileUrl(string key) =>
            $"https://objectstorage.{Region}.oraclecloud.com/n/{NamespaceName}/b/{BucketName}/o/{Uri.EscapeDataString(key)}";
    }
}
