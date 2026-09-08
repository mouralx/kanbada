import { BookOpen, ChevronDown, Search, X } from 'lucide-react';
import { useState } from 'react';
import { useI18n } from '../../shared/i18n';
import { helpArticles, helpCategories } from './helpContent';
const normalize = (value: string) =>
  value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
export function HelpCenter() {
  const { t, locale } = useI18n();
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('All topics');
  const language = locale === 'pt-PT' ? 'pt' : 'en';
  const results = helpArticles.filter(
    (article) =>
      (category === 'All topics' || article.category === category) &&
      normalize(article[language].join(' ')).includes(normalize(query.trim())),
  );
  return (
    <section className="help-center">
      <div className="help-hero">
        <BookOpen size={30} />
        <div>
          <span>{t('THE KANBADA GUIDE')}</span>
          <h2>{t('A little guidance. A lot of progress.')}</h2>
          <p>{t('Find practical answers for your everyday work.')}</p>
        </div>
      </div>
      <label className="help-search">
        <Search size={19} />
        <input
          aria-label={t('Search help')}
          placeholder={t('Search cards, sharing, dashboards…')}
          value={query}
          onChange={(e) => setQuery(e.target.value)}
        />
        {query && (
          <button aria-label={t('Clear help search')} onClick={() => setQuery('')}>
            <X size={17} />
          </button>
        )}
      </label>
      <div className="help-layout">
        <nav className="help-categories" aria-label={t('Help topics')}>
          {helpCategories.map(([en, pt]) => (
            <button key={en} aria-pressed={category === en} onClick={() => setCategory(en)}>
              <span>{language === 'pt' ? pt : en}</span>
              <small>
                {en === 'All topics'
                  ? helpArticles.length
                  : helpArticles.filter((a) => a.category === en).length}
              </small>
            </button>
          ))}
        </nav>
        <div>
          <p className="help-results" role="status">
            {t('{0} answers', results.length)}
          </p>
          <div className="help-articles">
            {results.map((article) => (
              <details key={article.id}>
                <summary>
                  {article[language][0]}
                  <ChevronDown size={16} />
                </summary>
                <p>{article[language][1]}</p>
              </details>
            ))}
          </div>
          {!results.length && (
            <div className="help-empty">
              <Search size={26} />
              <h3>{t('No matching answers')}</h3>
              <p>{t('Try another keyword or browse all topics.')}</p>
              <button
                className="secondary"
                onClick={() => {
                  setQuery('');
                  setCategory('All topics');
                }}
              >
                {t('Show all topics')}
              </button>
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
