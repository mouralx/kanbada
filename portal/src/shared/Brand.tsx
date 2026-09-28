import { useBranding } from './PlatformBranding';
export function Brand({ sidebar = false }: { sidebar?: boolean }) {
  const { branding } = useBranding();
  const logoOnly = branding.showName === false;
  return (
    <span
      className={`brand-identity${logoOnly ? ' brand-logo-only' : ''}`}
      role={logoOnly || sidebar ? 'img' : undefined}
      aria-label={logoOnly || sidebar ? branding.name : undefined}
    >
      {branding.logo ? (
        <img
          className={`platform-logo${sidebar && branding.collapsedLogo ? ' brand-expanded-logo' : ''}`}
          src={branding.logo}
          alt=""
        />
      ) : (
        <span
          className={`logo-symbol${sidebar && branding.collapsedLogo ? ' brand-expanded-logo' : ''}`}
          aria-hidden="true"
        >
          <i />
          <i />
          <i />
        </span>
      )}
      {sidebar && branding.collapsedLogo && (
        <img className="platform-logo brand-collapsed-logo" src={branding.collapsedLogo} alt="" />
      )}
      {!logoOnly && <span className="logo-word">{branding.name}</span>}
      {!logoOnly && branding.name === 'kanbada' && <span className="logo-dot">®</span>}
    </span>
  );
}
