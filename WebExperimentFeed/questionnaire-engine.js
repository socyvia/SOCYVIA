/* Reusable, deployment-defined participant questionnaire renderer and validator. */
(function(root){
  const stages=new Set(["PRE","POST"]);
  const types=new Set(["LIKERT","SINGLE_CHOICE","MULTIPLE_CHOICE","SHORT_TEXT","LONG_TEXT","NUMBER","YES_NO"]);
  const text={en:{required:"This question is required.",submit:"Continue",yes:"Yes",no:"No",progress:"Questionnaire"},ar:{required:"هذا السؤال مطلوب.",submit:"متابعة",yes:"نعم",no:"لا",progress:"استبيان"}};
  const localized=(value,language)=>typeof value==="string"?value:(value?.[language]||value?.en||value?.ar||"");
  const escape=value=>String(value??"").replace(/[&<>"']/g,char=>({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[char]));
  const configOf=item=>{try{return typeof item.configuration==="string"?JSON.parse(item.configuration||"{}"):item.configuration||{}}catch{return {}}};
  const localizedValue=(base,localizations,property)=>{const values={en:base},pascal=property.charAt(0).toUpperCase()+property.slice(1);if(localizations&&typeof localizations==="object")Object.entries(localizations).forEach(([language,value])=>{if(value&&typeof value==="object"&&(value[property]||value[pascal]))values[language]=value[property]||value[pascal]});return values};
  function normalise(raw){
    if(!raw||!stages.has(String(raw.stage||"").toUpperCase())||!Array.isArray(raw.items))return null;
    const items=raw.items.map((item,index)=>({id:String(item.id||""),type:String(item.type||"").toUpperCase(),question:localizedValue(item.question||"",item.localizations,"question"),description:localizedValue(item.description||"",item.localizations,"description"),required:!!item.required,order:Number.isFinite(item.order)?item.order:index+1,configuration:configOf(item)})).filter(item=>item.id&&types.has(item.type)).sort((a,b)=>a.order-b.order||a.id.localeCompare(b.id));
    return items.length?{id:String(raw.id||""),versionId:String(raw.versionId||""),stage:String(raw.stage).toUpperCase(),title:localizedValue(raw.title||"",raw.localizations,"title"),description:localizedValue(raw.description||"",raw.localizations,"description"),instructions:localizedValue(raw.instructions||"",raw.localizations,"instructions"),required:raw.required!==false,schemaVersion:String(raw.schemaVersion||"SOCYVIA.Questionnaire/1"),items}:null;
  }
  function isMissing(item,value){return value===undefined||value===null||value===""||(Array.isArray(value)&&value.length===0)}
  function validate(definition,answers){
    const errors={}; for(const item of definition.items){const value=answers?.[item.id];if(item.required&&isMissing(item,value)){errors[item.id]="required";continue}if(isMissing(item,value))continue;
      if(item.type==="LIKERT"||item.type==="NUMBER"){const numeric=Number(value),min=Number(item.configuration.minimum??item.configuration.min??-Infinity),max=Number(item.configuration.maximum??item.configuration.max??Infinity);if(!Number.isFinite(numeric)||numeric<min||numeric>max)errors[item.id]="invalid"}
      if(item.type==="SINGLE_CHOICE"&&!item.configuration.options?.some(option=>String(option.value??option)===String(value)))errors[item.id]="invalid";
      if(item.type==="MULTIPLE_CHOICE"&&(!Array.isArray(value)||value.some(answer=>!item.configuration.options?.some(option=>String(option.value??option)===String(answer)))))errors[item.id]="invalid";
      if(item.type==="YES_NO"&&value!==true&&value!==false)errors[item.id]="invalid";
    } return errors;
  }
  function inputName(item){return `question-${item.id}`}
  function optionLabel(option,lang){return localized(option.label??option,lang)||String(option.value??option)}
  function optionValue(option){return String(option.value??option)}
  function control(item,answers,lang){const value=answers?.[item.id],name=inputName(item),cfg=item.configuration;
    if(item.type==="LIKERT"){const min=Number(cfg.minimum??cfg.min??1),max=Number(cfg.maximum??cfg.max??5),numbers=[];for(let number=min;number<=max;number++)numbers.push(`<label><input type="radio" name="${name}" value="${number}" ${String(value)===String(number)?"checked":""}><span>${number}</span></label>`);return `<div class="likert-scale" style="--likert-count:${numbers.length}" role="radiogroup">${numbers.join("")}<div class="likert-labels"><span>${escape(localized(cfg.minimumLabel,lang))}</span><span>${escape(localized(cfg.maximumLabel,lang))}</span></div></div>`}
    if(item.type==="SINGLE_CHOICE")return `<div class="choice-list">${(cfg.options||[]).map(option=>`<label><input type="radio" name="${name}" value="${escape(optionValue(option))}" ${String(value)===optionValue(option)?"checked":""}><span>${escape(optionLabel(option,lang))}</span></label>`).join("")}</div>`;
    if(item.type==="MULTIPLE_CHOICE")return `<div class="choice-list">${(cfg.options||[]).map(option=>`<label><input type="checkbox" name="${name}" value="${escape(optionValue(option))}" ${(Array.isArray(value)&&value.map(String).includes(optionValue(option)))?"checked":""}><span>${escape(optionLabel(option,lang))}</span></label>`).join("")}</div>`;
    if(item.type==="YES_NO")return `<div class="choice-list inline-choice"><label><input type="radio" name="${name}" value="true" ${value===true?"checked":""}><span>${text[lang].yes}</span></label><label><input type="radio" name="${name}" value="false" ${value===false?"checked":""}><span>${text[lang].no}</span></label></div>`;
    if(item.type==="LONG_TEXT")return `<textarea id="${name}" name="${name}" rows="5">${escape(value||"")}</textarea>`;
    return `<input id="${name}" name="${name}" type="${item.type==="NUMBER"?"number":"text"}" value="${escape(value??"")}">`;
  }
  function render(definition,language="en",answers={},errors={}){const lang=language==="ar"?"ar":"en";return `<form class="questionnaire-form" data-questionnaire-stage="${definition.stage}" novalidate><p class="eyebrow">${text[lang].progress}</p><h1>${escape(localized(definition.title,lang))}</h1>${localized(definition.description,lang)?`<p class="questionnaire-description">${escape(localized(definition.description,lang))}</p>`:""}${localized(definition.instructions,lang)?`<p class="questionnaire-instructions">${escape(localized(definition.instructions,lang))}</p>`:""}<div class="questionnaire-items">${definition.items.map((item,index)=>`<fieldset class="questionnaire-item ${errors[item.id]?"has-error":""}"><legend><span>${index+1}.</span> ${escape(localized(item.question,lang))}${item.required?` <em aria-label="required">*</em>`:""}</legend>${localized(item.description,lang)?`<p>${escape(localized(item.description,lang))}</p>`:""}${control(item,answers,lang)}${errors[item.id]?`<small role="alert">${text[lang].required}</small>`:""}</fieldset>`).join("")}</div><button class="primary" type="submit">${text[lang].submit}</button></form>`}
  function values(form,definition){const data=new FormData(form),result={};for(const item of definition.items){const key=inputName(item);if(item.type==="MULTIPLE_CHOICE")result[item.id]=data.getAll(key);else if(item.type==="YES_NO"){const value=data.get(key);result[item.id]=value===null?null:value==="true"}else if(item.type==="LIKERT"||item.type==="NUMBER"){const value=data.get(key);result[item.id]=value===null||value===""?"":Number(value)}else result[item.id]=data.get(key)??""}return result}
  const api={normalise,validate,render,values,localized};root.SocyviaQuestionnaires=api;if(typeof module!=="undefined"&&module.exports)module.exports=api;
})(typeof window!=="undefined"?window:globalThis);
